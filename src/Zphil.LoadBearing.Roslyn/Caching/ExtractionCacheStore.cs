using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Zphil.LoadBearing.Rendering;

namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>The outcome of validating a persisted extraction cache against disk (Phase 11 WP6).</summary>
/// <remarks>
///     There is deliberately no <c>Disabled</c> member: whether the cache is consulted at all is a CLI-level
///     decision (the <c>--no-cache</c> flag / <c>LOADBEARING_CACHE_DIR</c> seam), so it lives on the CLI's
///     own outcome enum in a later work package — this Roslyn-layer store only knows Hit, Partial, and Miss.
/// </remarks>
internal enum CacheOutcome
{
    /// <summary>Every project is clean: the whole model can be rebuilt from cached fragments, no workspace.</summary>
    Hit,

    /// <summary>
    ///     Some projects are dirty: reuse the clean fragments, re-extract the
    ///     <see cref="CacheReadResult.DirtyProjects" />.
    /// </summary>
    Partial,

    /// <summary>Unusable (missing/garbled/version-mismatch/structural change): fall back to the full cold path.</summary>
    Miss
}

/// <summary>
///     The result of <see cref="ExtractionCacheStore.ReadAndValidate" />: what to reuse and what to redo.
///     Carries everything a caller needs to finish a run without re-reading the cache — the reusable
///     fragments, the dirty project set, and the recorded spec resolutions and workspace diagnostics
///     (replayed on a hit so cached and cold output are byte-identical on diagnostic-bearing solutions).
/// </summary>
internal sealed record CacheReadResult(
    CacheOutcome Outcome,
    IReadOnlyList<CodebaseFragment> ReusableFragments,
    IReadOnlySet<string> DirtyProjects,
    IReadOnlyList<SpecResolutionRecord> SpecResolutions,
    IReadOnlyList<string> Diagnostics)
{
    private static readonly IReadOnlySet<string> EmptySet = new HashSet<string>();

    internal static CacheReadResult Miss()
    {
        return new CacheReadResult(CacheOutcome.Miss, [], EmptySet, [], []);
    }

    internal static CacheReadResult Hit(
        IReadOnlyList<CodebaseFragment> fragments,
        IReadOnlyList<SpecResolutionRecord> specResolutions,
        IReadOnlyList<string> diagnostics)
    {
        return new CacheReadResult(CacheOutcome.Hit, fragments, EmptySet, specResolutions, diagnostics);
    }

    internal static CacheReadResult Partial(
        IReadOnlyList<CodebaseFragment> reusableFragments,
        IReadOnlySet<string> dirtyProjects,
        IReadOnlyList<SpecResolutionRecord> specResolutions,
        IReadOnlyList<string> diagnostics)
    {
        return new CacheReadResult(CacheOutcome.Partial, reusableFragments, dirtyProjects, specResolutions, diagnostics);
    }
}

/// <summary>
///     One project's identity as the caller knows it before any file is stated — the store fills in every
///     stamp and key itself, so the hashing lives in exactly one place and read/write agree by construction.
/// </summary>
/// <param name="ProjectName">The project (assembly) name.</param>
/// <param name="CsprojPath">The project file path.</param>
/// <param name="ProjectDirectory">The project directory (its cone is scanned for added <c>*.cs</c>).</param>
/// <param name="ProjectReferences">The names of the projects this one references (Merkle dependency edges).</param>
/// <param name="DocumentPaths">The project's source-document paths.</param>
internal sealed record ProjectInputs(
    string ProjectName,
    string CsprojPath,
    string ProjectDirectory,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> DocumentPaths);

/// <summary>
///     A pre-extraction fingerprint of the workspace's files: the structural stamps and per-project entries
///     (with content and Merkle keys) as they were before extraction started. Handed back to
///     <see cref="ExtractionCacheStore.Write" />, which re-stats every input and commits only if nothing has
///     changed since — the guard that stops a mid-run edit from poisoning the cache.
/// </summary>
internal sealed record CacheFingerprint(
    IReadOnlyList<FileStamp> StructuralStamps,
    IReadOnlyList<ProjectCacheEntry> Projects);

/// <summary>What a workspace-loaded run produced and wants persisted: the fragments plus their sidecar data.</summary>
internal sealed record ExtractionResult(
    IReadOnlyList<CodebaseFragment> Fragments,
    IReadOnlyList<SpecResolutionRecord> SpecResolutions,
    IReadOnlyList<string> Diagnostics);

/// <summary>
///     The read/validate/write boundary over one solution's persisted extraction cache — a single atomic
///     <c>cache.json</c> holding the manifest and every fragment (Phase 11 WP6). Validation runs with zero
///     MSBuild: it stats (and selectively re-hashes) the recorded inputs, scans each project cone for added
///     source, and recomputes the content/Merkle keys to produce a dirty set. This type is <em>unwired</em>
///     — no runner, pipeline, or CLI flag consults it yet; that is a later work package.
/// </summary>
/// <remarks>
///     <para>
///         <b>One atomic file.</b> A write goes to a sibling temp file and is then
///         <see cref="File.Move(string,string,bool)" />d
///         over the target, so a reader never sees a half-written file. Any torn, garbled, or hand-edited
///         content degrades to a parse-error <see cref="CacheOutcome.Miss" /> — the cache is disposable local
///         derived data, so unlike a baseline it has <b>no tamper story</b>: a bad file is simply ignored and
///         rebuilt, never a loud error and never a wrong answer.
///     </para>
///     <para>
///         <b>Racy-window and structural semantics</b> mirror the warm <see cref="WorkspaceSession" />'s
///         per-call reconcile sweep (the reference for probe chains, absence recording, and the cone scan),
///         reusing <see cref="FileFreshness" /> for the in-memory stat comparison. The one refinement a
///         persisted cache adds over the warm sweep is content-verification of structural files: a bare
///         mtime touch on a csproj whose bytes are unchanged is not a miss, because a false miss here costs a
///         full cold rebuild.
///     </para>
///     <para>
///         <b>Write discipline.</b> <see cref="CaptureFingerprint" /> is called before extraction and
///         <see cref="Write" /> after; <see cref="Write" /> re-stats every input and skips silently on any
///         delta. A hit that had to re-hash a settled file rewrites the manifest with promoted stamps so the
///         next validation takes the pure-stat fast path.
///     </para>
/// </remarks>
internal sealed class ExtractionCacheStore
{
    // v4 (post-review H1): CaptureFingerprint now computes cone-adds the same way validation does, so a
    // project whose cone holds an excluded stray *.cs no longer captures with adds=[] and then validates
    // dirty forever. A v3 cache.json carries content/Merkle keys built from that old adds=[] capture, so it
    // degrades to a clean Miss and is rebuilt with agreeing keys — the cache is disposable derived data,
    // never a loud error. (v3 added the member inventory; v2 added member-use edges over v1's type-only
    // fragments — every prior version likewise misses.)
    private const int CurrentSchemaVersion = 4;

    // Probe files whose presence anywhere from a project directory up to the solution directory changes the
    // build — recorded even when absent, so a newly-appearing one is an existence flip. Mirrors WorkspaceSession.
    private static readonly string[] StructuralProbeFileNames =
        ["Directory.Build.props", "Directory.Build.targets", "global.json"];

    // The tool version is the assembly's informational version (e.g. "0.1.0+<sha>"): the .NET SDK appends the
    // commit hash, giving per-commit invalidation during development, which is deliberate. Read from this
    // (Roslyn) assembly because the CLI's ServerVersion is not reachable from here; all packages ship lockstep,
    // so the value equals the tool's.
    private static readonly string CurrentToolVersion =
        typeof(ExtractionCacheStore).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    /// <summary>
    ///     The <see cref="JsonSerializerOptions" /> the cache serializes with — compact, with enums written as
    ///     their names (readability over the few bytes, and rename-safe: an unrecognized name degrades to a
    ///     parse-error miss). Exposed for the round-trip pin.
    /// </summary>
    internal static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly string cacheFilePath;
    private readonly string solutionPath;

    /// <summary>
    ///     Creates a store for <paramref name="solutionPath" />'s cache. The file lives under
    ///     <paramref name="cacheRootOverride" /> when given, else the default
    ///     <c>%LOCALAPPDATA%/Zphil.LoadBearing/cache</c> root; either way in a per-solution subdirectory keyed
    ///     by the solution's canonical path (see <see cref="CacheLocations" />).
    /// </summary>
    public ExtractionCacheStore(string solutionPath, string? cacheRootOverride = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        this.solutionPath = solutionPath;
        cacheFilePath = CacheLocations.CacheFilePath(solutionPath, cacheRootOverride);
    }

    /// <summary>
    ///     The number of file-content reads (SHA-256 computations) performed by validation. Zero in the
    ///     steady state, where every input is trusted on stat alone. Internal test observable — the
    ///     persisted-cache analog of <see cref="WorkspaceSession.SweepContentReads" />; never consulted in
    ///     production.
    /// </summary>
    internal long ContentHashCount { get; private set; }

    /// <summary>
    ///     Stats and hashes every input <em>now</em>, returning the pre-extraction fingerprint to hand to
    ///     <see cref="Write" /> after extraction. Does not touch <see cref="ContentHashCount" /> (that counts
    ///     validation reads only) and writes nothing.
    /// </summary>
    public CacheFingerprint CaptureFingerprint(IReadOnlyList<ProjectInputs> projects, CancellationToken ct = default)
    {
        var structuralStamps = new List<FileStamp>();
        var seenStructural = new HashSet<string>(PathComparison.Comparer);
        foreach (string path in EnumerateStructuralPaths(projects))
            if (seenStructural.Add(path))
                structuralStamps.Add(StampOf(path));

        var structuralShaByPath = BuildStructuralShaLookup(structuralStamps);

        var contentKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        var referencesByName = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var documentsByName = new Dictionary<string, IReadOnlyList<FileStamp>>(StringComparer.Ordinal);

        foreach (ProjectInputs project in projects)
        {
            ct.ThrowIfCancellationRequested();

            var documents = project.DocumentPaths.Select(StampOf).ToList();
            var documentShas = documents.Select(d => (d.Path, d.Sha256)).ToList();
            string? csprojSha = structuralShaByPath.GetValueOrDefault(Path.GetFullPath(project.CsprojPath));
            string? assetsSha = structuralShaByPath.GetValueOrDefault(AssetsPathOf(project.ProjectDirectory));

            // Compute cone-adds exactly as validation does, over the same known-document set (the stamps'
            // full paths). Hardcoding adds=[] here was the H1 bug: a *.cs on disk under the project but
            // excluded from compilation is a validation-time add, so an empty capture never validated and the
            // project stayed dirty forever. With capture and validation running the one routine, they agree.
            var knownDocuments = new HashSet<string>(documents.Select(d => d.Path), PathComparison.Comparer);
            var adds = ConeAdds(Path.GetFullPath(project.ProjectDirectory), knownDocuments);
            contentKeys[project.ProjectName] = ComputeContentKey(project.ProjectName, documentShas, csprojSha, assetsSha, adds);
            referencesByName[project.ProjectName] = project.ProjectReferences;
            documentsByName[project.ProjectName] = documents;
        }

        var memo = new Dictionary<string, string>(StringComparer.Ordinal);
        var entries = new List<ProjectCacheEntry>(projects.Count);
        foreach (ProjectInputs project in projects)
        {
            string merkleKey = ComputeMerkleKey(
                project.ProjectName, contentKeys, referencesByName, memo, new HashSet<string>(StringComparer.Ordinal));
            entries.Add(new ProjectCacheEntry(
                project.ProjectName,
                Path.GetFullPath(project.CsprojPath),
                Path.GetFullPath(project.ProjectDirectory),
                project.ProjectReferences,
                documentsByName[project.ProjectName],
                contentKeys[project.ProjectName],
                merkleKey));
        }

        return new CacheFingerprint(structuralStamps, entries);
    }

    /// <summary>
    ///     Commits <paramref name="extraction" /> to the cache, keyed to <paramref name="fingerprint" />.
    ///     Re-stats every fingerprinted input first; if any changed since <see cref="CaptureFingerprint" />
    ///     (a mid-run edit), skips the write and returns <c>false</c>. A best-effort I/O failure also returns
    ///     <c>false</c>. Otherwise writes atomically and returns <c>true</c>.
    /// </summary>
    public bool Write(CacheFingerprint fingerprint, ExtractionResult extraction, CancellationToken ct = default)
    {
        foreach (FileStamp stamp in fingerprint.StructuralStamps)
        {
            ct.ThrowIfCancellationRequested();
            if (StatChangedSinceCapture(stamp)) return false;
        }

        foreach (ProjectCacheEntry project in fingerprint.Projects)
        foreach (FileStamp document in project.Documents)
        {
            ct.ThrowIfCancellationRequested();
            if (StatChangedSinceCapture(document)) return false;
        }

        var manifest = new CacheManifest(
            CurrentSchemaVersion,
            CurrentToolVersion,
            fingerprint.StructuralStamps,
            fingerprint.Projects,
            extraction.SpecResolutions,
            extraction.Diagnostics,
            extraction.Fragments);

        return TryWriteAtomic(manifest);
    }

    /// <summary>
    ///     Reads and validates the cache against disk with zero MSBuild, in order: parse → schema/tool-version
    ///     → structural sweep (any existence flip or content change ⇒ miss) → per-document sweep + cone scan →
    ///     recomputed content/Merkle keys ⇒ dirty set. A hit that re-hashed a settled file rewrites promoted
    ///     stamps so the next call is pure-stat. Never throws (bar cancellation) — any failure is a miss.
    /// </summary>
    public CacheReadResult ReadAndValidate(CancellationToken ct = default)
    {
        try
        {
            return ValidateCore(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Any unexpected failure (I/O mid-scan, a malformed record STJ still bound) degrades to a miss:
            // the cache is disposable, so the run falls back to the cold path rather than surfacing an error.
            return CacheReadResult.Miss();
        }
    }

    private CacheReadResult ValidateCore(CancellationToken ct)
    {
        CacheManifest? manifest = TryRead();
        if (manifest is null) return CacheReadResult.Miss();
        if (manifest.SchemaVersion != CurrentSchemaVersion) return CacheReadResult.Miss();
        if (!string.Equals(manifest.ToolVersion, CurrentToolVersion, StringComparison.Ordinal)) return CacheReadResult.Miss();

        // Structural sweep — any existence flip or content change is a full miss.
        var refreshedStructural = new List<FileStamp>(manifest.StructuralStamps.Count);
        foreach (FileStamp stamp in manifest.StructuralStamps)
        {
            ct.ThrowIfCancellationRequested();
            (bool missed, FileStamp refreshed) = CheckStructural(stamp);
            if (missed) return CacheReadResult.Miss();
            refreshedStructural.Add(refreshed);
        }

        var structuralShaByPath = BuildStructuralShaLookup(refreshedStructural);

        // Per-document sweep + cone scan ⇒ each project's recomputed content key.
        var recomputedContentKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        var referencesByName = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var refreshedProjects = new List<ProjectCacheEntry>(manifest.Projects.Count);
        foreach (ProjectCacheEntry project in manifest.Projects)
        {
            ct.ThrowIfCancellationRequested();
            (string contentKey, var refreshedDocuments) = CheckProject(project, structuralShaByPath, ct);
            recomputedContentKeys[project.ProjectName] = contentKey;
            referencesByName[project.ProjectName] = project.ProjectReferences;
            refreshedProjects.Add(project with { Documents = refreshedDocuments });
        }

        // Recompute Merkle keys bottom-up; the dirty set is exactly the projects whose key no longer matches
        // — content-dirty projects plus every dependent reachable through the Merkle edges.
        var memo = new Dictionary<string, string>(StringComparer.Ordinal);
        var dirtyProjects = new HashSet<string>(StringComparer.Ordinal);
        foreach (ProjectCacheEntry project in manifest.Projects)
        {
            string recomputedMerkle = ComputeMerkleKey(
                project.ProjectName, recomputedContentKeys, referencesByName, memo, new HashSet<string>(StringComparer.Ordinal));
            if (!string.Equals(recomputedMerkle, project.MerkleKey, StringComparison.Ordinal))
                dirtyProjects.Add(project.ProjectName);
        }

        if (dirtyProjects.Count == 0)
        {
            PromoteIfChanged(manifest, refreshedStructural, refreshedProjects);
            return CacheReadResult.Hit(manifest.Fragments, manifest.SpecResolutions, manifest.Diagnostics);
        }

        var reusable = manifest.Fragments.Where(f => !dirtyProjects.Contains(f.ProjectName)).ToList();
        return CacheReadResult.Partial(reusable, dirtyProjects, manifest.SpecResolutions, manifest.Diagnostics);
    }

    // ── structural + document checks ────────────────────────────────────────────────────────────────────

    private (bool Missed, FileStamp Refreshed) CheckStructural(FileStamp stamp)
    {
        FileFreshness current = FileFreshness.Capture(stamp.Path);
        if (stamp.Exists != current.Exists) return (true, stamp); // an absent probe that appeared, or a vanished file
        if (!stamp.Exists) return (false, stamp); // both absent: unchanged

        if (ToFreshness(stamp).MatchesStat(current) && stamp.Promoted) return (false, stamp); // trusted on stat alone

        string? sha = HashDuringValidation(stamp.Path);
        if (sha is null || !string.Equals(sha, stamp.Sha256, StringComparison.Ordinal)) return (true, stamp); // real change

        // Content unchanged despite the stat delta (a bare touch): refresh so the next sweep is pure-stat.
        return (false, RefreshStamp(stamp.Path, current, sha));
    }

    private (string ContentKey, IReadOnlyList<FileStamp> RefreshedDocuments) CheckProject(
        ProjectCacheEntry project, IReadOnlyDictionary<string, string?> structuralShaByPath, CancellationToken ct)
    {
        var documentShas = new List<(string Path, string? Sha)>(project.Documents.Count);
        var refreshedDocuments = new List<FileStamp>(project.Documents.Count);
        var knownDocuments = new HashSet<string>(PathComparison.Comparer);

        foreach (FileStamp document in project.Documents)
        {
            ct.ThrowIfCancellationRequested();
            knownDocuments.Add(document.Path);

            FileFreshness current = FileFreshness.Capture(document.Path);
            if (!current.Exists)
            {
                // A deleted document is a content change for its project (the MISSING sentinel drives the key).
                documentShas.Add((document.Path, null));
                refreshedDocuments.Add(document with { Exists = false, LastWriteTimeUtcTicks = 0, Length = 0, Sha256 = null, Promoted = false });
                continue;
            }

            if (ToFreshness(document).MatchesStat(current) && document.Promoted)
            {
                documentShas.Add((document.Path, document.Sha256)); // provably unchanged: trust the recorded hash
                refreshedDocuments.Add(document);
                continue;
            }

            string? sha = HashDuringValidation(document.Path);
            documentShas.Add((document.Path, sha));
            refreshedDocuments.Add(RefreshStamp(document.Path, current, sha));
        }

        var adds = ConeAdds(project.ProjectDirectory, knownDocuments);
        string? csprojSha = structuralShaByPath.GetValueOrDefault(project.CsprojPath);
        string? assetsSha = structuralShaByPath.GetValueOrDefault(AssetsPathOf(project.ProjectDirectory));
        string contentKey = ComputeContentKey(project.ProjectName, documentShas, csprojSha, assetsSha, adds);
        return (contentKey, refreshedDocuments);
    }

    // Returns the project cone's *.cs not already known — the SDK-glob adds a bare mtime sweep cannot see.
    // Sorted so the content key is order-stable. Capture and validation both call this over the same known
    // set, so an always-present excluded stray lands in both adds lists and cancels; only a genuine add moves.
    private static IReadOnlyList<string> ConeAdds(string projectDirectory, HashSet<string> knownDocuments)
    {
        var adds = ProjectCone.Enumerate(projectDirectory).Where(full => !knownDocuments.Contains(full)).ToList();
        adds.Sort(StringComparer.Ordinal);
        return adds;
    }

    // ── keys ────────────────────────────────────────────────────────────────────────────────────────────

    // ContentKey(P) = hash over P's document hashes (path + sha, deleted ⇒ MISSING), its structural inputs
    // (csproj + assets hashes), and any cone-add paths. Changes iff P's own content changes. Sorted by path so
    // capture and validation agree regardless of input order.
    private static string ComputeContentKey(
        string projectName,
        IReadOnlyList<(string Path, string? Sha)> documents,
        string? csprojSha,
        string? assetsSha,
        IReadOnlyList<string> adds)
    {
        var builder = new StringBuilder();
        builder.Append("project\0").Append(projectName).Append('\n');
        foreach ((string path, string? sha) in documents.OrderBy(d => KeyPath(d.Path), StringComparer.Ordinal))
            builder.Append("doc\0").Append(KeyPath(path)).Append('\0').Append(sha ?? "MISSING").Append('\n');
        builder.Append("csproj\0").Append(csprojSha ?? "NONE").Append('\n');
        builder.Append("assets\0").Append(assetsSha ?? "NONE").Append('\n');
        foreach (string add in adds) // already ordinal-sorted
            builder.Append("add\0").Append(KeyPath(add)).Append('\n');
        return HashString(builder.ToString());
    }

    // MerkleKey(P) = hash(ContentKey(P), MerkleKey(dep) for each dep in name order). Memoized; a visiting set
    // guards against a (should-not-happen) reference cycle. A change to any dependency's content changes its
    // Merkle key and therefore every dependent's.
    private static string ComputeMerkleKey(
        string name,
        IReadOnlyDictionary<string, string> contentKeys,
        IReadOnlyDictionary<string, IReadOnlyList<string>> referencesByName,
        Dictionary<string, string> memo,
        HashSet<string> visiting)
    {
        if (memo.TryGetValue(name, out string? cached)) return cached;

        string self = contentKeys.GetValueOrDefault(name, "UNKNOWN");
        if (!visiting.Add(name)) return self; // cycle backstop

        var builder = new StringBuilder();
        builder.Append(self);
        if (referencesByName.TryGetValue(name, out var dependencies))
            foreach (string dependency in dependencies.OrderBy(d => d, StringComparer.Ordinal))
                if (contentKeys.ContainsKey(dependency))
                    builder.Append('\0').Append(ComputeMerkleKey(dependency, contentKeys, referencesByName, memo, visiting));

        visiting.Remove(name);
        string key = HashString(builder.ToString());
        memo[name] = key;
        return key;
    }

    // ── promotion + write ───────────────────────────────────────────────────────────────────────────────

    private void PromoteIfChanged(
        CacheManifest manifest, IReadOnlyList<FileStamp> refreshedStructural, IReadOnlyList<ProjectCacheEntry> refreshedProjects)
    {
        bool changed = !StampsEqual(manifest.StructuralStamps, refreshedStructural)
                       || !DocumentStampsEqual(manifest.Projects, refreshedProjects);
        if (!changed) return; // already reflects disk — steady state writes nothing

        CacheManifest promoted = manifest with { StructuralStamps = refreshedStructural, Projects = refreshedProjects };
        TryWriteAtomic(promoted); // best-effort; a later change is still caught by the next validation's stat delta
    }

    private bool TryWriteAtomic(CacheManifest manifest)
    {
        try
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
            AtomicFile.WriteAllBytes(cacheFilePath, bytes);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false; // the cache is disposable — a failed write is simply rebuilt next run, never an error
        }
    }

    private CacheManifest? TryRead()
    {
        if (!File.Exists(cacheFilePath)) return null;

        try
        {
            byte[] bytes = File.ReadAllBytes(cacheFilePath);
            return JsonSerializer.Deserialize<CacheManifest>(bytes, JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null; // torn / garbled / unreadable ⇒ miss
        }
    }

    // ── stamping + hashing ──────────────────────────────────────────────────────────────────────────────

    private static FileStamp StampOf(string path)
    {
        string full = Path.GetFullPath(path);
        FileFreshness fresh = FileFreshness.Capture(full);
        if (!fresh.Exists) return new FileStamp(full, false, 0, 0, null, false);

        return new FileStamp(full, true, fresh.LastWriteTimeUtc.Ticks, fresh.Length, TryHashFile(full), fresh.IsPromoted);
    }

    private static FileStamp RefreshStamp(string path, FileFreshness current, string? sha)
    {
        return new FileStamp(path, current.Exists, current.LastWriteTimeUtc.Ticks, current.Length, sha, current.IsPromoted);
    }

    private static bool StatChangedSinceCapture(FileStamp stamp)
    {
        FileFreshness current = FileFreshness.Capture(stamp.Path);
        if (stamp.Exists != current.Exists) return true;
        if (!stamp.Exists) return false;
        return !ToFreshness(stamp).MatchesStat(current);
    }

    private string? HashDuringValidation(string path)
    {
        ContentHashCount++;
        return TryHashFile(path);
    }

    private static string? TryHashFile(string path)
    {
        try
        {
            return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string HashString(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    // ── structural enumeration (mirrors WorkspaceSession) ───────────────────────────────────────────────

    private IEnumerable<string> EnumerateStructuralPaths(IReadOnlyList<ProjectInputs> projects)
    {
        string fullSolution = Path.GetFullPath(solutionPath);
        yield return fullSolution;

        foreach (ProjectInputs project in projects)
        {
            yield return Path.GetFullPath(project.CsprojPath);

            string projectDirectory = Path.GetFullPath(project.ProjectDirectory);
            yield return AssetsPathOf(projectDirectory);

            foreach (string ancestor in ProjectCone.Ancestors(projectDirectory))
            foreach (string probe in StructuralProbeFileNames)
                yield return Path.Combine(ancestor, probe);
        }
    }

    private static string AssetsPathOf(string projectDirectory)
    {
        return Path.GetFullPath(Path.Combine(projectDirectory, "obj", "project.assets.json"));
    }

    // ── small helpers ───────────────────────────────────────────────────────────────────────────────────

    private static Dictionary<string, string?> BuildStructuralShaLookup(IReadOnlyList<FileStamp> stamps)
    {
        var map = new Dictionary<string, string?>(PathComparison.Comparer);
        foreach (FileStamp stamp in stamps) map[stamp.Path] = stamp.Sha256;
        return map;
    }

    private static FileFreshness ToFreshness(FileStamp stamp)
    {
        // RecordedAtUtc is irrelevant to MatchesStat, the only comparison we use it for; the racy verdict is
        // carried by the persisted Promoted flag, not re-derived from a runtime instant.
        return new FileFreshness(stamp.Exists, new DateTime(stamp.LastWriteTimeUtcTicks, DateTimeKind.Utc), stamp.Length, default);
    }

    private static string KeyPath(string path)
    {
        return PathComparison.Comparison == StringComparison.OrdinalIgnoreCase ? path.ToLowerInvariant() : path;
    }

    private static bool StampsEqual(IReadOnlyList<FileStamp> left, IReadOnlyList<FileStamp> right)
    {
        if (left.Count != right.Count) return false;
        for (var i = 0; i < left.Count; i++)
            if (left[i] != right[i])
                return false; // FileStamp is a record ⇒ scalar value equality
        return true;
    }

    private static bool DocumentStampsEqual(IReadOnlyList<ProjectCacheEntry> left, IReadOnlyList<ProjectCacheEntry> right)
    {
        if (left.Count != right.Count) return false;
        for (var i = 0; i < left.Count; i++)
            if (!StampsEqual(left[i].Documents, right[i].Documents))
                return false;
        return true;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions { WriteIndented = false };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}