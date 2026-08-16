using System.Text.Json;
using Microsoft.CodeAnalysis;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn.Caching;

namespace Zphil.LoadBearing.Roslyn.Replay;

/// <summary>The three states <see cref="BinlogCaptureStore.Validate" /> can report.</summary>
internal enum CaptureState
{
    /// <summary>No capture exists for this solution; the run takes the cold path with no notice.</summary>
    Absent,

    /// <summary>The capture still reflects the tree's structure; replay the recorded binlog copy.</summary>
    Usable,

    /// <summary>A capture existed but no longer holds; carry the notice and fall back to a design-time build.</summary>
    Invalid
}

/// <summary>
///     The result of <see cref="BinlogCaptureStore.Validate" />. <see cref="BinlogCopyPath" /> is set only on
///     <see cref="CaptureState.Usable" /> (the copy to replay); <see cref="Notice" /> only on
///     <see cref="CaptureState.Invalid" /> — a complete user-facing line, carrying no <c>warning: </c> prefix
///     of its own.
/// </summary>
internal sealed record CaptureValidation(CaptureState State, string? BinlogCopyPath, string? Notice)
{
    internal static CaptureValidation Absent()
    {
        return new CaptureValidation(CaptureState.Absent, null, null);
    }

    internal static CaptureValidation Usable(string binlogCopyPath)
    {
        return new CaptureValidation(CaptureState.Usable, binlogCopyPath, null);
    }

    internal static CaptureValidation Invalid(string notice)
    {
        return new CaptureValidation(CaptureState.Invalid, null, notice);
    }
}

/// <summary>
///     The ingest/validate boundary over one solution's persisted <b>build capture</b> — a copied binlog plus
///     a structure-only-keyed manifest that lets a later run replay the build with no design-time build, as
///     long as the tree's structure has not moved. It is the sibling of the
///     <see cref="ExtractionCacheStore" /> and mirrors its disciplines exactly.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why a second, structure-only cache layer.</b> The fragment cache keys on structure
///         <em>and</em> per-document content, so any source edit re-extracts. The capture keys on structure
///         alone, because replay reads source text from current disk — content edits are invisible to it and
///         must stay valid. What invalidates a capture is anything that changes the captured csc command
///         lines' meaning: a csproj/sln/props/targets/global.json/assets edit (the structural stamps), a
///         source add (the project-cone scan), or a source/obj-generated file that has gone missing (the
///         document existence sweep — a <c>dotnet clean</c> deletes <c>*.GlobalUsings.g.cs</c> and friends,
///         which replay cannot regenerate, so the capture must go invalid rather than drift).
///     </para>
///     <para>
///         <b>Ingest is explicit and sanity-checked.</b> A binlog older than the tree's structural files, or
///         one that does not cover exactly the solution's csproj set, is a loud
///         <see cref="UserErrorException" /> refusal — the
///         capture contract is "from a build of the current tree", and a subset binlog would silently shrink
///         the model forever. A capture invalidated <em>later</em> is not loud: <see cref="Validate" />
///         reports an <see cref="CaptureState.Invalid" /> notice and the run falls back to a design-time
///         build, because a hard error would break CI on any csproj change until re-capture.
///     </para>
///     <para>
///         <b>Persistence is best-effort.</b> The caller already holds its replayed solution, so an I/O
///         failure while persisting must not fail the run — <see cref="Ingest" /> returns whether it
///         persisted. The binlog copy is written <em>before</em> the manifest, so a torn write leaves a
///         manifest-less orphan (validated as <see cref="CaptureState.Absent" />), never a manifest pointing
///         at a missing binlog.
///     </para>
/// </remarks>
internal sealed class BinlogCaptureStore
{
    // The manifest schema this store reads and writes. A manifest written under any other version degrades
    // to one UnreadableNotice and re-captures — acceptable for disposable derived data, never a wrong answer.
    private const int CurrentSchemaVersion = 3;

    /// <summary>The <see cref="CaptureState.Invalid" /> notice for a garbled/torn/missing-copy/schema case.</summary>
    internal const string UnreadableNotice =
        "build capture is unreadable; running a design-time build instead. Re-capture: rebuild with -bl and "
        + "re-run with --binlog.";

    /// <summary>The <see cref="CaptureState.Invalid" /> notice when the capture is from another tool version.</summary>
    internal const string VersionMismatchNotice =
        "build capture was written by a different LoadBearing version; running a design-time build instead. "
        + "Re-capture: rebuild with -bl and re-run with --binlog.";

    private readonly string cacheDirectory;
    private readonly string captureBinlogPath;
    private readonly string captureManifestPath;
    private readonly string solutionPath;

    /// <summary>
    ///     Creates a store for <paramref name="solutionPath" />'s capture. The files live under
    ///     <paramref name="cacheRootOverride" /> when given, else the default cache root — either way in the
    ///     per-solution subdirectory <see cref="CacheLocations" /> derives. Never reads an environment
    ///     variable itself: <c>LOADBEARING_CACHE_DIR</c> arrives here already resolved through an
    ///     <c>IEnvironment</c> seam (self-spec <c>mcp/env-through-seam</c>).
    /// </summary>
    public BinlogCaptureStore(string solutionPath, string? cacheRootOverride = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        this.solutionPath = solutionPath;
        captureManifestPath = CacheLocations.CaptureManifestPath(solutionPath, cacheRootOverride);
        captureBinlogPath = CacheLocations.CaptureBinlogPath(solutionPath, cacheRootOverride);
        cacheDirectory = Path.GetDirectoryName(captureManifestPath)!;
    }

    /// <summary>
    ///     The number of file-content reads (SHA-256 computations) performed by <see cref="Validate" />. Zero
    ///     in the steady state, where every structural input is trusted on stat alone. Internal test
    ///     observable — the capture's analog of <see cref="ExtractionCacheStore.ContentHashCount" />; never
    ///     consulted in production.
    /// </summary>
    internal long ContentHashCount { get; private set; }

    // ── message factories (dictated text; exposed so tests pin without duplicating format logic) ──────────

    /// <summary>The <see cref="UserErrorException" /> text when a structural file is newer than the binlog.</summary>
    internal static string StaleAtIngestMessage(string binlogArgument, string newestStructuralFile)
    {
        return $"--binlog '{binlogArgument}' predates '{newestStructuralFile}' (the build no longer reflects "
               + "the current tree). Rebuild with -bl and pass the fresh binlog.";
    }

    /// <summary>The refusal text when the binlog is missing one or more of the solution's csproj members.</summary>
    internal static string MissingCoverageMessage(string binlogArgument, IEnumerable<string> missingCsprojs)
    {
        string list = string.Join("\n", missingCsprojs.OrderBy(p => p, StringComparer.Ordinal).Select(p => $"  {p}"));
        return $"--binlog '{binlogArgument}' does not cover the solution; missing from the binlog:\n{list}\n"
               + "Build the whole solution with -bl and pass that binlog.";
    }

    /// <summary>The refusal text when the capture target is a solution filter (.slnf) rather than a full solution.</summary>
    internal static string SolutionFilterNotSupportedMessage(string solutionFileName)
    {
        return $"'{solutionFileName}' is a solution filter (.slnf); build captures require the full solution. "
               + "Pass the .sln/.slnx instead.";
    }

    /// <summary>The refusal text when the binlog contains a project the solution does not list.</summary>
    internal static string ExtraCoverageMessage(
        string binlogArgument, string solutionFileName, IEnumerable<string> extraCsprojs)
    {
        string list = string.Join("\n", extraCsprojs.OrderBy(p => p, StringComparer.Ordinal).Select(p => $"  {p}"));
        return $"--binlog '{binlogArgument}' contains projects that are not in '{solutionFileName}':\n{list}\n"
               + "Pass a .binlog produced by building exactly this solution.";
    }

    /// <summary>The <see cref="CaptureState.Invalid" /> notice when a structural input no longer matches.</summary>
    internal static string StaleNotice(string offendingFile)
    {
        return $"build capture is stale ('{offendingFile}' no longer matches the capture); running a "
               + "design-time build instead. Re-capture: rebuild with -bl and re-run with --binlog.";
    }

    // ── ingest ───────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    ///     Sanity-checks <paramref name="replayedSolution" /> against <paramref name="binlogFullPath" /> and,
    ///     if it passes, persists the capture. In order: (1) refuse if any structural file is strictly newer
    ///     than the binlog (a stale build); (2) refuse if the binlog does not cover exactly the solution's
    ///     csproj set; (3) copy the binlog and write the manifest, both atomically and best-effort.
    /// </summary>
    /// <param name="replayedSolution">The solution just produced by replaying <paramref name="binlogFullPath" />.</param>
    /// <param name="binlogFullPath">The absolute path to the binlog that was replayed.</param>
    /// <param name="binlogArgument">The user's as-typed <c>--binlog</c> value, used verbatim in refusals.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if the capture was persisted; <c>false</c> if a best-effort I/O step failed.</returns>
    /// <exception cref="UserErrorException">The binlog is stale, or does not cover exactly the solution.</exception>
    public bool Ingest(
        Solution replayedSolution, string binlogFullPath, string binlogArgument, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binlogFullPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(binlogArgument);

        // A solution filter narrows a build to a subset; a capture keyed on it would silently shrink the
        // model, and a replay owns no workspace to announce the narrowing from. Refuse it explicitly and
        // early. The cold .slnf path via MSBuildWorkspace is unaffected — it loads honestly and declares
        // what it left out — because this guard is only about persisting a build capture.
        //
        // The early refusal is also what keeps RefuseIfCoverageMismatch's unguarded ReadCsprojMembers below
        // safe: that is deliberately the raw-membership reader, which runs the classic-.sln regex over a
        // filter's JSON and gets zero members. Reaching it with a .slnf would refuse for a nonsense reason.
        if (solutionPath.EndsWith(".slnf", StringComparison.OrdinalIgnoreCase))
            throw new UserErrorException(SolutionFilterNotSupportedMessage(Path.GetFileName(solutionPath)));

        var projects = CollectProjects(replayedSolution);
        var structuralPaths = DedupedStructuralPaths(projects);

        RefuseIfStale(structuralPaths, binlogFullPath, binlogArgument, ct);
        RefuseIfCoverageMismatch(projects, binlogArgument);

        return TryPersist(projects, structuralPaths, binlogFullPath, ct);
    }

    private void RefuseIfStale(
        IReadOnlyList<string> structuralPaths, string binlogFullPath, string binlogArgument, CancellationToken ct)
    {
        DateTime binlogTime = File.GetLastWriteTimeUtc(binlogFullPath);

        string? newestOffender = null;
        DateTime newestOffenderTime = default;
        foreach (string path in structuralPaths)
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(path)) continue;

            DateTime writeTime = File.GetLastWriteTimeUtc(path);
            if (writeTime <= binlogTime) continue; // equal timestamps tolerate coarse filesystem clocks

            bool newer = newestOffender is null
                         || writeTime > newestOffenderTime
                         || (writeTime == newestOffenderTime && string.CompareOrdinal(path, newestOffender) < 0);
            if (newer)
            {
                newestOffender = path;
                newestOffenderTime = writeTime;
            }
        }

        if (newestOffender is not null)
            throw new UserErrorException(StaleAtIngestMessage(binlogArgument, newestOffender));
    }

    private void RefuseIfCoverageMismatch(IReadOnlyList<CaptureProjectEntry> projects, string binlogArgument)
    {
        var solutionCsprojs = SolutionProjectFileParser.ReadCsprojMembers(solutionPath);
        var replayCsprojs = projects.Select(p => p.CsprojPath).ToList();

        var replayCanonical = new HashSet<string>(replayCsprojs.Select(CanonicalKey), PathComparison.Comparer);
        var missing = solutionCsprojs.Where(p => !replayCanonical.Contains(CanonicalKey(p))).ToList();
        if (missing.Count > 0)
            throw new UserErrorException(MissingCoverageMessage(binlogArgument, missing));

        var solutionCanonical = new HashSet<string>(solutionCsprojs.Select(CanonicalKey), PathComparison.Comparer);
        var extra = replayCsprojs.Where(p => !solutionCanonical.Contains(CanonicalKey(p))).ToList();
        if (extra.Count > 0)
            throw new UserErrorException(
                ExtraCoverageMessage(binlogArgument, Path.GetFileName(solutionPath), extra));
    }

    private bool TryPersist(
        IReadOnlyList<CaptureProjectEntry> projects, IReadOnlyList<string> structuralPaths,
        string binlogFullPath, CancellationToken ct)
    {
        List<FileStamp> structuralStamps;
        try
        {
            Directory.CreateDirectory(cacheDirectory);
            structuralStamps = structuralPaths.Select(FileStamping.StampOf).ToList();

            // Binlog copy BEFORE the manifest: a torn state is then a manifest-less orphan (validated Absent),
            // never a manifest pointing at a binlog that was never written.
            AtomicFile.Copy(binlogFullPath, captureBinlogPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }

        ct.ThrowIfCancellationRequested();
        var manifest = new CaptureManifest(
            CurrentSchemaVersion, FileStamping.CurrentToolVersion, structuralStamps, projects, FileStamping.StampOf(captureBinlogPath));
        return TryWriteManifestAtomic(manifest);
    }

    // ── validate ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    ///     Validates the persisted capture against disk with zero MSBuild. Reports <see cref="CaptureState.Absent" />
    ///     (no capture — silent cold path), <see cref="CaptureState.Usable" /> (replay the binlog copy), or
    ///     <see cref="CaptureState.Invalid" /> (a notice to print, then fall back). Existence flips and
    ///     content changes on structural inputs invalidate; a bare mtime touch with unchanged bytes does not
    ///     (hash-verified on a stat mismatch, then the manifest is rewritten with promoted stamps so the next
    ///     run is pure-stat). Every recorded document must still exist, and no <c>*.cs</c> may have appeared
    ///     in a project cone. Never throws (bar cancellation): an unexpected failure is the unreadable variant
    ///     — a capture must never break a run.
    /// </summary>
    public CaptureValidation Validate(CancellationToken ct = default)
    {
        try
        {
            return ValidateCore(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Any unexpected failure (I/O mid-sweep, a malformed record STJ still bound) degrades to the
            // unreadable variant: the capture is disposable, so the run falls back rather than surfacing it.
            // The filter is the whole cancellation clause — it names what this handler is NOT for, so a
            // cancellation travels on untouched.
            return CaptureValidation.Invalid(UnreadableNotice);
        }
    }

    private CaptureValidation ValidateCore(CancellationToken ct)
    {
        if (!File.Exists(captureManifestPath)) return CaptureValidation.Absent();

        CaptureManifest? manifest = TryReadManifest();
        if (manifest is null) return CaptureValidation.Invalid(UnreadableNotice);
        if (manifest.SchemaVersion != CurrentSchemaVersion) return CaptureValidation.Invalid(UnreadableNotice);
        if (!string.Equals(manifest.ToolVersion, FileStamping.CurrentToolVersion, StringComparison.Ordinal))
            return CaptureValidation.Invalid(VersionMismatchNotice);

        // The binlog we would replay must still be on disk (a torn write or a hand-deleted copy).
        if (!File.Exists(captureBinlogPath)) return CaptureValidation.Invalid(UnreadableNotice);

        // Structural sweep — an existence flip or content change is the stale variant, naming the file.
        var refreshedStructural = new List<FileStamp>(manifest.StructuralStamps.Count);
        foreach (FileStamp stamp in manifest.StructuralStamps)
        {
            ct.ThrowIfCancellationRequested();
            (bool changed, FileStamp refreshed) = FileStamping.CheckStructural(stamp, () => ContentHashCount++);
            if (changed) return CaptureValidation.Invalid(StaleNotice(stamp.Path));
            refreshedStructural.Add(refreshed);
        }

        // Every recorded document (source and obj-generated) must still exist — a delete or clean is stale.
        foreach (CaptureProjectEntry project in manifest.Projects)
        foreach (string documentPath in project.DocumentPaths)
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(documentPath)) return CaptureValidation.Invalid(StaleNotice(documentPath));
        }

        // Cone scan for a *.cs added outside bin/obj that the capture did not record — a membership change.
        foreach (CaptureProjectEntry project in manifest.Projects)
        {
            ct.ThrowIfCancellationRequested();
            if (FirstConeAdd(project) is { } added) return CaptureValidation.Invalid(StaleNotice(added));
        }

        PromoteIfChanged(manifest, refreshedStructural);
        return CaptureValidation.Usable(captureBinlogPath);
    }

    // The first cone add — a *.cs present in neither the recorded ConeFiles nor the compiled DocumentPaths,
    // i.e. the SDK-glob add a stat sweep cannot see. An excluded stray that was in the cone at ingest is in
    // ConeFiles, so it is not read as an add; only a file new since ingest trips this. ProjectCone.Adds owns
    // the scan and its ordinal sort, so "first" means the same thing here as it does to the fragment cache.
    private static string? FirstConeAdd(CaptureProjectEntry project)
    {
        var known = new HashSet<string>(project.DocumentPaths, PathComparison.Comparer);
        known.UnionWith(project.ConeFiles);

        return ProjectCone.Adds(project.ProjectDirectory, known).FirstOrDefault();
    }

    private void PromoteIfChanged(CaptureManifest manifest, IReadOnlyList<FileStamp> refreshedStructural)
    {
        if (FileStamping.StampsEqual(manifest.StructuralStamps, refreshedStructural)) return; // already reflects disk

        CaptureManifest promoted = manifest with { StructuralStamps = refreshedStructural };
        TryWriteManifestAtomic(promoted); // best-effort; a later change is still caught by the next stat delta
    }

    // ── project collection ───────────────────────────────────────────────────────────────────────────────

    // One entry per C# project (collapsing a multi-target-framework project's several Projects onto the one
    // name the load boundary normalized them to, as SolutionCacheInputs does), with the FULL document set —
    // obj-generated sources included, deliberately.
    private static IReadOnlyList<CaptureProjectEntry> CollectProjects(Solution solution)
    {
        var byName = new Dictionary<string, Accumulator>(StringComparer.Ordinal);

        foreach (Project project in solution.Projects)
        {
            if (project.Language != LanguageNames.CSharp || project.FilePath is null) continue;

            string csprojFull = Path.GetFullPath(project.FilePath);
            if (!byName.TryGetValue(project.Name, out Accumulator? accumulator))
            {
                accumulator = new Accumulator(
                    project.Name,
                    csprojFull,
                    Path.GetDirectoryName(csprojFull)!,
                    project.OutputFilePath,
                    project.CompilationOutputInfo.AssemblyPath);
                byName[project.Name] = accumulator;
            }

            foreach (Document document in project.Documents)
                if (document.FilePath is not null)
                    accumulator.Documents.Add(Path.GetFullPath(document.FilePath));
        }

        return byName.Values
            .OrderBy(a => a.ProjectName, StringComparer.Ordinal)
            .Select(a => a.ToEntry())
            .ToList();
    }

    // ── structural enumeration (mirrors ExtractionCacheStore) ────────────────────────────────────────────

    private IReadOnlyList<string> DedupedStructuralPaths(IReadOnlyList<CaptureProjectEntry> projects)
    {
        var paths = new List<string>();
        var seen = new HashSet<string>(PathComparison.Comparer);
        foreach (string path in EnumerateStructuralPaths(projects))
            if (seen.Add(path))
                paths.Add(path);
        return paths;
    }

    private IEnumerable<string> EnumerateStructuralPaths(IReadOnlyList<CaptureProjectEntry> projects)
    {
        string fullSolution = Path.GetFullPath(solutionPath);
        yield return fullSolution;

        foreach (CaptureProjectEntry project in projects)
        {
            yield return project.CsprojPath;

            // The assets file then the ancestor × probe cross-product, in ProjectCone's order — the same
            // recipe and the same order the fragment cache stamps, so the two cannot disagree about what
            // "the structure moved" means.
            foreach (string path in ProjectCone.StructuralPaths(
                         project.ProjectDirectory, project.EvaluatedOutputPath, project.IntermediateAssemblyPath))
                yield return path;
        }
    }

    // ── read + atomic write (mirrors ExtractionCacheStore) ───────────────────────────────────────────────

    private CaptureManifest? TryReadManifest()
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(captureManifestPath);
            return JsonSerializer.Deserialize<CaptureManifest>(bytes, ExtractionCacheStore.JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null; // torn / garbled / unreadable ⇒ unreadable notice
        }
    }

    private bool TryWriteManifestAtomic(CaptureManifest manifest)
    {
        try
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, ExtractionCacheStore.JsonOptions);
            AtomicFile.WriteAllBytes(captureManifestPath, bytes);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false; // the capture is disposable — a failed write is re-captured next run, never an error
        }
    }

    // ── small helpers ────────────────────────────────────────────────────────────────────────────────────

    private static string CanonicalKey(string path)
    {
        return PathCanonicalizer.Resolve(path);
    }

    // Accumulates one project's identity and its unioned, ordinal-sorted document set across frameworks.
    private sealed class Accumulator(
        string projectName,
        string csprojPath,
        string projectDirectory,
        string? evaluatedOutputPath,
        string? intermediateAssemblyPath)
    {
        public string ProjectName { get; } = projectName;
        public SortedSet<string> Documents { get; } = new(StringComparer.Ordinal);

        public CaptureProjectEntry ToEntry()
        {
            // Snapshot the cone at ingest so a later scan can tell a genuine add from an already-excluded stray.
            var coneFiles = ProjectCone.Enumerate(projectDirectory).OrderBy(p => p, StringComparer.Ordinal).ToList();
            return new CaptureProjectEntry(
                ProjectName,
                csprojPath,
                projectDirectory,
                Documents.ToList(),
                coneFiles,
                evaluatedOutputPath,
                intermediateAssemblyPath);
        }
    }
}
