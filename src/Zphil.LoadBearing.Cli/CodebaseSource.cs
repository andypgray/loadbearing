using Microsoft.CodeAnalysis;
using Zphil.LoadBearing.Cli.Mcp.Infrastructure;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Caching;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     How a <see cref="CodebaseSource" /> produced its model — the internal test observable for the
///     persisted extraction cache (Phase 11 D2). Never printed: stdout/stderr stay byte-identical to a cold
///     run in every mode, so this only ever tells a test which path a run took.
/// </summary>
internal enum CodebaseSourceOutcome
{
    /// <summary>Every project was clean: the model was merged from cached fragments with no workspace opened.</summary>
    Hit,

    /// <summary>Some projects were dirty: the clean fragments were reused, only the dirty ones re-extracted.</summary>
    Partial,

    /// <summary>No usable cache: the whole model was extracted from a freshly loaded workspace and written back.</summary>
    Miss,

    /// <summary>
    ///     The cache was bypassed entirely (<c>--no-cache</c>, or no cache location) — a pure cold run, no read, no
    ///     write.
    /// </summary>
    Disabled
}

/// <summary>
///     The check/status/graph extraction seam over the persisted extraction cache (Phase 11 D2). It carries
///     everything a run needs before the codebase itself — the loaded <see cref="Model" /> and spec
///     <see cref="Resolution" /> (both absent for the spec-less <c>graph</c> survey), the discovered
///     <see cref="SolutionPath" />, and the workspace <see cref="Diagnostics" /> — and defers the codebase to
///     a lazy <see cref="ExtractAsync" /> so the pipeline can still fail fast on a tampered baseline before
///     any extraction. On a cache <see cref="CodebaseSourceOutcome.Hit" /> the model is merged from persisted
///     fragments with no workspace, no MSBuild, and no design-time build; otherwise a workspace is acquired
///     through the injected <see cref="ISolutionSource" />, every C# project is extracted (the clean ones
///     reused on a partial), the requested subset is merged, and the whole set is written back.
/// </summary>
/// <remarks>
///     <para>
///         <b>Correctness over speed.</b> The cache is disposable local derived data with no tamper story:
///         a read only ever hits when the store has validated every input against disk, a fragment merge is
///         the one code path a cold run also takes (so the model is identical by construction), and every
///         write is best-effort — any doubt or failure degrades to a plain cold extraction, never a wrong
///         answer. On a hit the spec is replayed without a workspace: an explicit DLL through
///         <see cref="SpecResolver.TryResolveWithoutSolution" />, a convention/csproj spec through the
///         recorded resolution re-run over <see cref="SpecResolver.RequireBuiltOutput" /> (so the
///         sibling-configuration fallback and its error text match a cold run); a spec with no matching
///         record falls back to the cold path.
///     </para>
///     <para>
///         <b>The warm MCP path leaves the persisted cache untouched.</b> Tool calls pass <c>--no-cache</c>
///         (a <see cref="CodebaseSourceOutcome.Disabled" /> run), so <c>cache.json</c> and the warm
///         <see cref="WorkspaceSession" /> keep independent lifetimes and never race on the file. That
///         Disabled branch is not always a full cold walk, though: when the handle carries a warm fragment
///         extractor (<see cref="SolutionHandle.WarmFragments" />, Phase 12 D2) it merges the session store's
///         reused-plus-re-extracted fragments instead, so a warm re-check re-walks only the projects whose
///         bytes changed — while the model stays byte-identical to a cold run through the one merge path.
///     </para>
/// </remarks>
internal sealed class CodebaseSource : IDisposable
{
    /// <summary>The CLI-side environment variable overriding the cache root (also the test-isolation knob).</summary>
    internal const string CacheDirectoryVariable = "LOADBEARING_CACHE_DIR";

    private readonly CacheReadResult cacheRead;
    private readonly SolutionHandle? handle;
    private readonly ArchitectureModel? model;
    private readonly string normalizedSpecArgument;
    private readonly SpecResolution? resolution;
    private readonly ExtractionCacheStore? store;

    private HashSet<string> reExtractedProjects = new(StringComparer.Ordinal);

    private CodebaseSource(
        CodebaseSourceOutcome outcome,
        string solutionPath,
        IReadOnlyList<string> diagnostics,
        ArchitectureModel? model,
        SpecResolution? resolution,
        SolutionHandle? handle,
        ExtractionCacheStore? store,
        CacheReadResult cacheRead,
        string normalizedSpecArgument)
    {
        Outcome = outcome;
        SolutionPath = solutionPath;
        Diagnostics = diagnostics;
        this.model = model;
        this.resolution = resolution;
        this.handle = handle;
        this.store = store;
        this.cacheRead = cacheRead;
        this.normalizedSpecArgument = normalizedSpecArgument;
    }

    /// <summary>Which path this run took. Internal test observable; never printed.</summary>
    internal CodebaseSourceOutcome Outcome { get; }

    /// <summary>The finalized architecture model. Absent for the spec-less <c>graph</c> survey.</summary>
    public ArchitectureModel Model =>
        model ?? throw new InvalidOperationException("This codebase source was created without a spec (graph is spec-less).");

    /// <summary>The resolved spec (DLL + optional excluded project). Absent for the spec-less <c>graph</c> survey.</summary>
    public SpecResolution Resolution =>
        resolution ?? throw new InvalidOperationException("This codebase source was created without a spec (graph is spec-less).");

    /// <summary>Workspace-load diagnostics — freshly collected on a cold run, replayed from the cache on a hit.</summary>
    public IReadOnlyList<string> Diagnostics { get; }

    /// <summary>Absolute path to the discovered <c>.sln</c>/<c>.slnx</c>.</summary>
    public string SolutionPath { get; }

    /// <summary>The solution directory — baselines and diff resolution anchor here.</summary>
    public string SolutionDirectory => Path.GetDirectoryName(SolutionPath)!;

    /// <summary>
    ///     The projects extracted from the workspace on this run (all of them on a miss, only the dirty set
    ///     on a partial, none on a hit or disabled run). Internal test observable for the partial re-extraction pin.
    /// </summary>
    internal IReadOnlySet<string> ReExtractedProjects => reExtractedProjects;

    /// <summary>Disposes the owned cold workspace; a no-op on a cache hit (which owns none).</summary>
    public void Dispose()
    {
        handle?.Dispose();
    }

    /// <summary>
    ///     Discovers the solution and prepares a spec-ful source for <c>check</c>/<c>status</c>: on a cache
    ///     hit the spec is replayed and the model loaded with no workspace; otherwise a workspace is acquired
    ///     and the spec resolved against it. Discovery, spec-resolution, and spec-load failures surface
    ///     exactly as a cold run raises them.
    /// </summary>
    public static async Task<CodebaseSource> CreateWithSpecAsync(
        ISolutionSource source,
        IEnvironment environment,
        string? solution,
        string? spec,
        string workingDirectory,
        bool noCache,
        CancellationToken ct)
    {
        string solutionPath = ModelPipeline.DiscoverSolution(solution, workingDirectory);
        string normalized = NormalizeSpecArgument(spec);
        ExtractionCacheStore? store = noCache ? null : TryCreateStore(solutionPath, environment);

        if (store is null)
            return await CreateColdWithSpecAsync(
                source, solutionPath, spec, normalized, null, CacheReadResult.Miss(),
                CodebaseSourceOutcome.Disabled, ct);

        CacheReadResult read = store.ReadAndValidate(ct);
        if (read.Outcome == CacheOutcome.Hit
            && ResolveSpecOnHit(spec, read.SpecResolutions) is { } hitResolution)
        {
            ArchitectureModel hitModel = ModelPipeline.LoadModel(hitResolution.DllPath);
            return new CodebaseSource(
                CodebaseSourceOutcome.Hit, solutionPath, read.Diagnostics, hitModel, hitResolution,
                null, store, read, normalized);
        }

        // A miss, a partial, or a hit whose spec was not recorded: acquire the workspace and resolve cold.
        CodebaseSourceOutcome coldOutcome =
            read.Outcome == CacheOutcome.Partial ? CodebaseSourceOutcome.Partial : CodebaseSourceOutcome.Miss;
        return await CreateColdWithSpecAsync(source, solutionPath, spec, normalized, store, read, coldOutcome, ct);
    }

    /// <summary>
    ///     Discovers the solution and prepares a spec-less source for <c>graph</c>: a cache hit merges every
    ///     cached fragment with no workspace; otherwise a workspace is acquired and the whole codebase extracted.
    /// </summary>
    public static async Task<CodebaseSource> CreateSpeclessAsync(
        ISolutionSource source,
        IEnvironment environment,
        string? solution,
        string workingDirectory,
        bool noCache,
        CancellationToken ct)
    {
        string solutionPath = ModelPipeline.DiscoverSolution(solution, workingDirectory);
        ExtractionCacheStore? store = noCache ? null : TryCreateStore(solutionPath, environment);

        if (store is null)
            return await CreateColdSpeclessAsync(
                source, solutionPath, null, CacheReadResult.Miss(), CodebaseSourceOutcome.Disabled, ct);

        CacheReadResult read = store.ReadAndValidate(ct);
        if (read.Outcome == CacheOutcome.Hit)
            return new CodebaseSource(
                CodebaseSourceOutcome.Hit, solutionPath, read.Diagnostics, null, null,
                null, store, read, "");

        CodebaseSourceOutcome coldOutcome =
            read.Outcome == CacheOutcome.Partial ? CodebaseSourceOutcome.Partial : CodebaseSourceOutcome.Miss;
        return await CreateColdSpeclessAsync(source, solutionPath, store, read, coldOutcome, ct);
    }

    /// <summary>
    ///     Produces the codebase model, excluding <paramref name="excludeProjectName" /> (the spec project,
    ///     or null for <c>graph</c>). On a hit the cached fragments are merged directly; otherwise the
    ///     workspace is fingerprinted, the needed projects are extracted (the clean ones reused on a partial),
    ///     the subset is merged for the model, and the whole fragment set is written back best-effort.
    /// </summary>
    public async Task<CodebaseModel> ExtractAsync(string? excludeProjectName, CancellationToken ct)
    {
        if (Outcome == CodebaseSourceOutcome.Hit)
            return FragmentMerger.Merge(Retain(cacheRead.ReusableFragments, excludeProjectName));

        Solution solution = handle!.Solution;

        if (store is null) // Disabled: no persisted cache — either a pure cold walk or the warm incremental path.
        {
            if (handle.WarmFragments is { } warmFragments)
            {
                // The warm MCP path: the session-scoped store reuses clean projects' fragments and re-walks
                // only the dirty ∪ dependent set, terminating in the same FragmentMerger every path uses.
                // Exclusion is the merge-time Retain, so one store serves every tool whatever it drops; the
                // store's re-extraction set becomes this source's observable so the runner counters keep meaning.
                SessionFragmentSet warm = await warmFragments(ct);
                reExtractedProjects = new HashSet<string>(warm.ReExtractedProjects, StringComparer.Ordinal);
                return FragmentMerger.Merge(Retain(warm.Fragments, excludeProjectName));
            }

            IReadOnlyCollection<string>? exclude = excludeProjectName is null ? null : [excludeProjectName];
            return await CodebaseExtractor.ExtractFromSolutionAsync(solution, exclude, ct);
        }

        // Fingerprint before extraction so a mid-run edit is caught by the store's re-stat at write time.
        CacheFingerprint? fingerprint = TryCaptureFingerprint(solution, ct);

        var allFragments = await ExtractAllFragmentsAsync(solution, ct);
        CodebaseModel merged = FragmentMerger.Merge(Retain(allFragments, excludeProjectName));

        if (fingerprint is not null)
            TryWrite(solution, fingerprint, allFragments, ct);

        return merged;
    }

    // On a partial, extract only the dirty projects and reuse the clean fragments; on a miss, extract them
    // all. Either way the result is the full fragment set in ordinal project order, so the merge and the
    // write-back order match a cold run exactly.
    private async Task<List<CodebaseFragment>> ExtractAllFragmentsAsync(Solution solution, CancellationToken ct)
    {
        if (Outcome == CodebaseSourceOutcome.Partial)
        {
            var reExtracted =
                await CodebaseExtractor.ExtractFragmentsAsync(solution, cacheRead.DirtyProjects, ct);
            reExtractedProjects = new HashSet<string>(cacheRead.DirtyProjects, StringComparer.Ordinal);
            return cacheRead.ReusableFragments
                .Concat(reExtracted)
                .OrderBy(f => f.ProjectName, StringComparer.Ordinal)
                .ToList();
        }

        List<CodebaseFragment> all = [.. await CodebaseExtractor.ExtractFragmentsAsync(solution, null, ct)];
        reExtractedProjects = all.Select(f => f.ProjectName).ToHashSet(StringComparer.Ordinal);
        return all;
    }

    // ── construction helpers ──────────────────────────────────────────────────────────────────────────────

    private static async Task<CodebaseSource> CreateColdWithSpecAsync(
        ISolutionSource source, string solutionPath, string? spec, string normalizedSpec,
        ExtractionCacheStore? store, CacheReadResult cacheRead, CodebaseSourceOutcome outcome, CancellationToken ct)
    {
        SolutionHandle handle = await AcquireAsync(source, solutionPath, ct);
        try
        {
            SpecResolution resolution = SpecResolver.Resolve(handle.Solution, spec);
            ArchitectureModel model = ModelPipeline.LoadModel(resolution.DllPath);
            return new CodebaseSource(
                outcome, solutionPath, handle.Diagnostics, model, resolution, handle, store, cacheRead, normalizedSpec);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static async Task<CodebaseSource> CreateColdSpeclessAsync(
        ISolutionSource source, string solutionPath, ExtractionCacheStore? store, CacheReadResult cacheRead,
        CodebaseSourceOutcome outcome, CancellationToken ct)
    {
        SolutionHandle handle = await AcquireAsync(source, solutionPath, ct);
        return new CodebaseSource(
            outcome, solutionPath, handle.Diagnostics, null, null, handle, store, cacheRead,
            "");
    }

    // The solution is already discovered, so hand the acquired path straight to the source: discovery over an
    // explicit file is idempotent, and a warm source still reconciles the snapshot for it.
    private static Task<SolutionHandle> AcquireAsync(ISolutionSource source, string solutionPath, CancellationToken ct)
    {
        return source.AcquireAsync(solutionPath, Path.GetDirectoryName(solutionPath)!, ct);
    }

    // ── spec replay on a hit ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    ///     Resolves the spec on a cache hit without a workspace, or returns null when the cold path is needed.
    ///     An explicit DLL resolves directly (a missing one throws the same loud error a cold run would); a
    ///     convention or csproj spec replays its recorded resolution, re-running the built-output check so the
    ///     sibling-configuration fallback and its error text match cold; a spec with no matching record returns
    ///     null so the caller reloads the workspace.
    /// </summary>
    internal static SpecResolution? ResolveSpecOnHit(string? spec, IReadOnlyList<SpecResolutionRecord> records)
    {
        if (SpecResolver.TryResolveWithoutSolution(spec) is { } dllResolution) return dllResolution;

        string normalized = NormalizeSpecArgument(spec);
        SpecResolutionRecord? record = records.FirstOrDefault(r => string.Equals(r.NormalizedSpecArgument, normalized, StringComparison.Ordinal));
        if (record is null) return null;

        string dll = SpecResolver.RequireBuiltOutput(record.ExcludeProjectName ?? normalized, record.OutputFilePath);
        return new SpecResolution(dll, record.ExcludeProjectName);
    }

    // ── cache write ───────────────────────────────────────────────────────────────────────────────────────

    private CacheFingerprint? TryCaptureFingerprint(Solution solution, CancellationToken ct)
    {
        try
        {
            var inputs = SolutionCacheInputs.Collect(solution);
            return store!.CaptureFingerprint(inputs, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null; // could not fingerprint ⇒ skip the write and keep the correct, freshly extracted model
        }
    }

    private void TryWrite(Solution solution, CacheFingerprint fingerprint, IReadOnlyList<CodebaseFragment> allFragments, CancellationToken ct)
    {
        try
        {
            var records = BuildWriteSpecRecords(solution);
            store!.Write(fingerprint, new ExtractionResult(allFragments, records, Diagnostics), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The cache is disposable derived data: a write failure must never change the run's result.
        }
    }

    // The spec record to persist for this run, unioned onto the manifest's existing records (replacing any
    // with the same normalized argument). Only a solution-member spec (convention/csproj — it excludes a
    // project) is recorded, since that is the resolution a hit cannot replay without one; an explicit DLL
    // resolves on a hit with no record, and graph has no spec at all.
    private IReadOnlyList<SpecResolutionRecord> BuildWriteSpecRecords(Solution solution)
    {
        var existing = cacheRead.SpecResolutions;
        if (resolution?.ExcludeProjectName is not { } excludeName) return existing;

        string? outputFilePath = solution.Projects
            .FirstOrDefault(p => string.Equals(p.Name, excludeName, StringComparison.Ordinal))?.OutputFilePath;

        var record = new SpecResolutionRecord(normalizedSpecArgument, excludeName, outputFilePath);
        return existing
            .Where(r => !string.Equals(r.NormalizedSpecArgument, normalizedSpecArgument, StringComparison.Ordinal))
            .Append(record)
            .ToList();
    }

    // ── small helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static List<CodebaseFragment> Retain(IReadOnlyList<CodebaseFragment> fragments, string? excludeProjectName)
    {
        return fragments
            .Where(f => excludeProjectName is null || !string.Equals(f.ProjectName, excludeProjectName, StringComparison.Ordinal))
            .ToList();
    }

    private static string NormalizeSpecArgument(string? spec)
    {
        return string.IsNullOrWhiteSpace(spec) ? "" : Path.GetFullPath(spec);
    }

    private static ExtractionCacheStore? TryCreateStore(string solutionPath, IEnvironment environment)
    {
        try
        {
            string? cacheRoot = environment.GetVariable(CacheDirectoryVariable);
            return new ExtractionCacheStore(solutionPath, string.IsNullOrWhiteSpace(cacheRoot) ? null : cacheRoot);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null; // no resolvable cache location ⇒ run cold, no read and no write
        }
    }
}