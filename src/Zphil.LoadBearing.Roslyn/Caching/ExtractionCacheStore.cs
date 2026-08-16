using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Zphil.LoadBearing.Rendering;

namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>The outcome of validating a persisted extraction cache against disk.</summary>
/// <remarks>
///     There is deliberately no <c>Disabled</c> member: whether the cache is consulted at all is a CLI-level
///     decision (the <c>--no-cache</c> flag / <c>LOADBEARING_CACHE_DIR</c> seam), so it lives on the CLI's
///     own outcome enum — this Roslyn-layer store only knows Hit, Partial, and Miss.
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
///     Carries everything a caller needs to finish a run without re-reading the cache.
/// </summary>
/// <remarks>
///     The recorded spec resolutions, workspace diagnostics and failed projects are replayed on a hit, so
///     cached and cold output — and the fail-closed verdict — are identical on a solution that does not load
///     completely.
/// </remarks>
internal sealed record CacheReadResult(
    CacheOutcome Outcome,
    IReadOnlyList<CodebaseFragment> ReusableFragments,
    IReadOnlySet<string> DirtyProjects,
    IReadOnlyList<SpecResolutionRecord> SpecResolutions,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<string> FailedProjects,
    IReadOnlyList<string> UncheckedProjects)
{
    private static readonly IReadOnlySet<string> EmptySet = new HashSet<string>();

    internal static CacheReadResult Miss()
    {
        return new CacheReadResult(CacheOutcome.Miss, [], EmptySet, [], [], [], []);
    }

    internal static CacheReadResult Hit(
        IReadOnlyList<CodebaseFragment> fragments,
        IReadOnlyList<SpecResolutionRecord> specResolutions,
        IReadOnlyList<string> diagnostics,
        IReadOnlyList<string> failedProjects,
        IReadOnlyList<string> uncheckedProjects)
    {
        return new CacheReadResult(
            CacheOutcome.Hit, fragments, EmptySet, specResolutions, diagnostics, failedProjects,
            uncheckedProjects);
    }

    internal static CacheReadResult Partial(
        IReadOnlyList<CodebaseFragment> reusableFragments,
        IReadOnlySet<string> dirtyProjects,
        IReadOnlyList<SpecResolutionRecord> specResolutions,
        IReadOnlyList<string> diagnostics,
        IReadOnlyList<string> failedProjects,
        IReadOnlyList<string> uncheckedProjects)
    {
        return new CacheReadResult(
            CacheOutcome.Partial, reusableFragments, dirtyProjects, specResolutions, diagnostics, failedProjects,
            uncheckedProjects);
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
/// <param name="EvaluatedOutputPath">
///     The project's evaluated output path, or null when the workspace carried none. Paired with
///     <paramref name="IntermediateAssemblyPath" /> it is what locates the restore assets file under a
///     non-default output layout (see <see cref="IntermediateOutputTree" />).
/// </param>
/// <param name="IntermediateAssemblyPath">The project's intermediate assembly path, or null when unknown.</param>
internal sealed record ProjectInputs(
    string ProjectName,
    string CsprojPath,
    string ProjectDirectory,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> DocumentPaths,
    string? EvaluatedOutputPath = null,
    string? IntermediateAssemblyPath = null);

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
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<string> FailedProjects,
    IReadOnlyList<string> UncheckedProjects);

/// <summary>
///     The read/validate/write boundary over one solution's persisted extraction cache — a single atomic
///     <c>cache.json</c> holding the manifest and every fragment. Validation runs with zero
///     MSBuild: it stats (and selectively re-hashes) the recorded inputs, scans each project cone for added
///     source, and recomputes the content/Merkle keys to produce a dirty set.
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
    // The on-disk schema of this per-solution cache file. A record written under any other version degrades to
    // a clean Miss — the cache is disposable derived data, so a schema it cannot read is rebuilt, never a loud
    // error. Bump this whenever a fragment gains a fact, or a hit would deserialize the new field as its
    // default and answer with a fact the extraction never recorded.
    private const int CurrentSchemaVersion = 18;

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
                structuralStamps.Add(FileStamping.StampOf(path));

        var structuralShaByPath = BuildStructuralShaLookup(structuralStamps);

        var contentKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        var referencesByName = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var documentsByName = new Dictionary<string, IReadOnlyList<FileStamp>>(StringComparer.Ordinal);

        foreach (ProjectInputs project in projects)
        {
            ct.ThrowIfCancellationRequested();

            var documents = project.DocumentPaths.Select(FileStamping.StampOf).ToList();
            var documentShas = documents.Select(d => (d.Path, d.Sha256)).ToList();
            string? csprojSha = structuralShaByPath.GetValueOrDefault(Path.GetFullPath(project.CsprojPath));
            string? assetsSha = structuralShaByPath.GetValueOrDefault(FileStamping.AssetsPathOf(project.ProjectDirectory));

            // Compute cone-adds exactly as validation does, over the same known-document set (the stamps'
            // full paths). Hardcoding an empty adds list here would be wrong: a *.cs on disk under the
            // project but excluded from compilation is a validation-time add, so the capture would never
            // validate and the project would stay dirty forever. With capture and validation running the one
            // routine, they agree.
            var knownDocuments = new HashSet<string>(documents.Select(d => d.Path), PathComparison.Comparer);
            var adds = ProjectCone.Adds(Path.GetFullPath(project.ProjectDirectory), knownDocuments);
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
            FileStamping.CurrentToolVersion,
            fingerprint.StructuralStamps,
            fingerprint.Projects,
            extraction.SpecResolutions,
            extraction.Diagnostics,
            extraction.FailedProjects,
            extraction.UncheckedProjects,
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Any unexpected failure (I/O mid-scan, a malformed record STJ still bound) degrades to a miss:
            // the cache is disposable, so the run falls back to the cold path rather than surfacing an error.
            // The filter is the whole cancellation clause — it names what this handler is NOT for, so a
            // cancellation travels on untouched.
            return CacheReadResult.Miss();
        }
    }

    private CacheReadResult ValidateCore(CancellationToken ct)
    {
        CacheManifest? manifest = TryRead();
        if (manifest is null) return CacheReadResult.Miss();
        if (manifest.SchemaVersion != CurrentSchemaVersion) return CacheReadResult.Miss();
        if (!string.Equals(manifest.ToolVersion, FileStamping.CurrentToolVersion, StringComparison.Ordinal)) return CacheReadResult.Miss();

        // Structural sweep — any existence flip or content change is a full miss.
        var refreshedStructural = new List<FileStamp>(manifest.StructuralStamps.Count);
        foreach (FileStamp stamp in manifest.StructuralStamps)
        {
            ct.ThrowIfCancellationRequested();
            (bool missed, FileStamp refreshed) = FileStamping.CheckStructural(stamp, () => ContentHashCount++);
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
            return CacheReadResult.Hit(
                manifest.Fragments, manifest.SpecResolutions, manifest.Diagnostics, manifest.FailedProjects,
                manifest.UncheckedProjects);
        }

        var reusable = manifest.Fragments.Where(f => !dirtyProjects.Contains(f.ProjectName)).ToList();
        return CacheReadResult.Partial(
            reusable, dirtyProjects, manifest.SpecResolutions, manifest.Diagnostics, manifest.FailedProjects,
            manifest.UncheckedProjects);
    }

    // ── structural + document checks ────────────────────────────────────────────────────────────────────

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

            if (FileStamping.ToFreshness(document).MatchesStat(current) && document.Promoted)
            {
                documentShas.Add((document.Path, document.Sha256)); // provably unchanged: trust the recorded hash
                refreshedDocuments.Add(document);
                continue;
            }

            string? sha = HashDuringValidation(document.Path);
            documentShas.Add((document.Path, sha));
            refreshedDocuments.Add(FileStamping.RefreshStamp(document.Path, current, sha));
        }

        // Capture and validation compute cone-adds through the one routine over the same known-document set,
        // so an always-present excluded stray lands in both adds lists and cancels; only a genuine add moves.
        var adds = ProjectCone.Adds(project.ProjectDirectory, knownDocuments);
        string? csprojSha = structuralShaByPath.GetValueOrDefault(project.CsprojPath);
        string? assetsSha = structuralShaByPath.GetValueOrDefault(FileStamping.AssetsPathOf(project.ProjectDirectory));
        string contentKey = ComputeContentKey(project.ProjectName, documentShas, csprojSha, assetsSha, adds);
        return (contentKey, refreshedDocuments);
    }

    // ── keys ────────────────────────────────────────────────────────────────────────────────────────────

    // ContentKey(P) = hash over P's document hashes (path + sha, deleted ⇒ MISSING), its structural inputs
    // (csproj + assets hashes), and any cone-add paths. Changes iff P's own content changes. Sorted by path so
    // capture and validation agree regardless of input order. Fed to the digest a line at a time rather than
    // as one assembled string: a large project's key would otherwise materialize a megabyte-scale string and
    // its UTF-8 copy, both LOH-bound, per project per run. The byte sequence and its order are load-bearing:
    // change either and every persisted cache file stops matching.
    private static string ComputeContentKey(
        string projectName,
        IReadOnlyList<(string Path, string? Sha)> documents,
        string? csprojSha,
        string? assetsSha,
        IReadOnlyList<string> adds)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, $"project\0{projectName}\n");

        // Folded once per document, then sorted on the folded form — one fold per document rather than one
        // per comparison.
        var keyed = documents
            .Select(d => (Path: PathComparison.Fold(d.Path), d.Sha))
            .OrderBy(d => d.Path, StringComparer.Ordinal);
        foreach ((string path, string? sha) in keyed)
            Append(hash, $"doc\0{path}\0{sha ?? "MISSING"}\n");

        Append(hash, $"csproj\0{csprojSha ?? "NONE"}\n");
        Append(hash, $"assets\0{assetsSha ?? "NONE"}\n");
        foreach (string add in adds) // already ordinal-sorted
            Append(hash, $"add\0{PathComparison.Fold(add)}\n");

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, string line)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(line));
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
        string key = FileStamping.HashText(builder.ToString());
        memo[name] = key;
        return key;
    }

    // ── promotion + write ───────────────────────────────────────────────────────────────────────────────

    private void PromoteIfChanged(
        CacheManifest manifest, IReadOnlyList<FileStamp> refreshedStructural, IReadOnlyList<ProjectCacheEntry> refreshedProjects)
    {
        bool changed = !FileStamping.StampsEqual(manifest.StructuralStamps, refreshedStructural)
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

    private static bool StatChangedSinceCapture(FileStamp stamp)
    {
        FileFreshness current = FileFreshness.Capture(stamp.Path);
        if (stamp.Exists != current.Exists) return true;
        if (!stamp.Exists) return false;
        return !FileStamping.ToFreshness(stamp).MatchesStat(current);
    }

    private string? HashDuringValidation(string path)
    {
        ContentHashCount++;
        return FileStamping.TryHashFile(path);
    }

    // ── structural enumeration ──────────────────────────────────────────────────────────────────────────

    private IEnumerable<string> EnumerateStructuralPaths(IReadOnlyList<ProjectInputs> projects)
    {
        string fullSolution = Path.GetFullPath(solutionPath);
        yield return fullSolution;

        // Under a .slnf the file above is the filter, not the solution. Editing the solution changes what a
        // run loads — adding a member the filter selects, or any member at all when its projects array is
        // empty — while leaving the filter's own bytes and timestamp untouched, so without this the edit
        // never dirties the cache and the stale answer is served indefinitely.
        if (SolutionProjectFileParser.TryReadReferencedSolution(fullSolution) is { } referencedSolution)
            yield return referencedSolution;

        foreach (ProjectInputs project in projects)
        {
            yield return Path.GetFullPath(project.CsprojPath);

            foreach (string path in ProjectCone.StructuralPaths(
                         Path.GetFullPath(project.ProjectDirectory),
                         project.EvaluatedOutputPath,
                         project.IntermediateAssemblyPath))
                yield return path;
        }
    }

    // ── small helpers ───────────────────────────────────────────────────────────────────────────────────

    private static Dictionary<string, string?> BuildStructuralShaLookup(IReadOnlyList<FileStamp> stamps)
    {
        var map = new Dictionary<string, string?>(PathComparison.Comparer);
        foreach (FileStamp stamp in stamps) map[stamp.Path] = stamp.Sha256;
        return map;
    }

    private static bool DocumentStampsEqual(IReadOnlyList<ProjectCacheEntry> left, IReadOnlyList<ProjectCacheEntry> right)
    {
        if (left.Count != right.Count) return false;
        for (var i = 0; i < left.Count; i++)
            if (!FileStamping.StampsEqual(left[i].Documents, right[i].Documents))
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
