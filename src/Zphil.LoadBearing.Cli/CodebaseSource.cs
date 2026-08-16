using Microsoft.CodeAnalysis;
using Zphil.LoadBearing.Cli.Mcp.Infrastructure;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Caching;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     How a <see cref="CodebaseSource" /> produced its model — the internal test observable for the
///     persisted extraction cache. Never printed: stdout/stderr stay byte-identical to a cold
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
///     The one model-acquisition seam, shared by every verb and tool that reads this solution. It carries
///     everything a run needs before the codebase itself — the loaded <see cref="Model" /> and spec
///     <see cref="Resolution" /> (both absent for the spec-less <c>graph</c> survey), the discovered
///     <see cref="SolutionPath" />, and the workspace <see cref="Diagnostics" /> — and defers the codebase to
///     a lazy <see cref="ExtractAsync" /> so the pipeline can still fail fast on a tampered baseline before
///     any extraction. A workspace, where the run still needs one, is acquired through the injected
///     <see cref="ISolutionSource" />.
/// </summary>
/// <remarks>
///     <para>
///         <b>Correctness over speed.</b> The cache is disposable local derived data with no tamper story:
///         a read only ever hits when the store has validated every input against disk, a fragment merge is
///         the one code path a cold run also takes (so the model is identical by construction), and every
///         write is best-effort — any doubt or failure degrades to a plain cold extraction, never a wrong
///         answer. On a hit the spec is replayed without a workspace: an explicit DLL through
///         <see cref="SpecResolver.TryResolveWithoutSolution" />, a convention/csproj spec through the
///         recorded resolution re-run over
///         <see cref="SpecResolver.RequireBuiltOutput" /> (so the bounded
///         search from the output root, what that search refuses, and its error text all match a cold run); a
///         spec with no matching record falls back to the cold path.
///     </para>
///     <para>
///         <b>The warm MCP path leaves the persisted cache untouched.</b> Tool calls pass <c>--no-cache</c>
///         (a <see cref="CodebaseSourceOutcome.Disabled" /> run), so <c>cache.json</c> and the warm
///         <see cref="WorkspaceSession" /> keep independent lifetimes and never race on the file. That
///         Disabled branch is not always a full cold walk, though: when the handle carries a warm codebase
///         producer (<see cref="SolutionHandle.WarmCodebase" />) it takes the session store's model instead,
///         so a warm re-check re-walks only the projects whose bytes changed — and re-merges only when one
///         of them did.
///     </para>
///     <para>
///         <b>
///             Which verbs front the persisted cache is a policy, not an accident of which ladder they were
///             written on.
///         </b>
///         <c>check</c>, <c>status</c> and <c>graph</c> do
///         (<see cref="CreateWithSpecAsync(ISolutionSource,IEnvironment,string,string,string,bool,CancellationToken)" />
///         and <see cref="CreateSpeclessAsync" /> take the caller's <c>--no-cache</c>); <c>render</c>,
///         <c>baseline</c>, <c>explain</c> and the <c>arch_context</c> tool do not
///         (<see cref="CreateWithSpecAsync(ISolutionSource,string,string,string,CancellationToken)" />).
///         They all acquire here either way, so the warm path, the merge notes and the incomplete-model
///         diagnostics are one thing taught once rather than a policy each ladder learns separately.
///     </para>
/// </remarks>
internal sealed class CodebaseSource : IDisposable
{
    private readonly CacheReadResult cacheRead;

    // The solution's declared membership, read at most once per source and handed to every extraction path so
    // each project it collects carries ProjectNode.SolutionMember. Resolution needs the same set (see
    // SpecResolver.Resolve), so the cold spec path forces its own reader and hands it in rather than reading
    // twice; every other path mints one here and leaves it unforced. Lazy because a cache hit merges stored
    // fragments and never extracts, and the point of a hit is that it touches nothing it does not have to.
    private readonly Lazy<IReadOnlySet<string>?> declaredMembers;

    private readonly SolutionHandle? handle;
    private readonly WorkspaceDiagnostics loadDiagnostics;
    private readonly ArchitectureModel? model;
    private readonly string normalizedSpecArgument;
    private readonly SpecResolution? resolution;
    private readonly ExtractionCacheStore? store;

    // The advisory merge notes the last ExtractAsync produced (same-FQN cross-project conflation),
    // regenerated from the fragments on every path — a cache hit re-merges, so these need no persistence.
    // Empty until ExtractAsync runs, which is why Diagnostics is composed per read rather than captured.
    private IReadOnlyList<string> mergeNotes = [];

    private HashSet<string> reExtractedProjects = new(StringComparer.Ordinal);

    // A null declaredMembers mints the unforced reader; the cold spec path passes the one it already forced.
    private CodebaseSource(
        CodebaseSourceOutcome outcome,
        string solutionPath,
        WorkspaceDiagnostics loadDiagnostics,
        ArchitectureModel? model,
        SpecResolution? resolution,
        SolutionHandle? handle,
        ExtractionCacheStore? store,
        CacheReadResult cacheRead,
        string normalizedSpecArgument,
        Lazy<IReadOnlySet<string>?>? declaredMembers = null)
    {
        Outcome = outcome;
        SolutionPath = solutionPath;
        SolutionName = Path.GetFileName(solutionPath);
        SolutionDirectory = SolutionProjectFileParser.AnchorDirectory(solutionPath);
        this.declaredMembers = declaredMembers
                               ?? new Lazy<IReadOnlySet<string>?>(() => SpecExclusion.TryReadDeclaredMembers(solutionPath));
        this.loadDiagnostics = loadDiagnostics;
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

    /// <summary>The resolved spec (DLL + excluded projects). Absent for the spec-less <c>graph</c> survey.</summary>
    public SpecResolution Resolution =>
        resolution ?? throw new InvalidOperationException("This codebase source was created without a spec (graph is spec-less).");

    /// <summary>
    ///     How well the workspace loaded: the projects that failed to load, the ones whose NuGet packages are
    ///     not in the model, and the load-failure diagnostics — all freshly collected on a cold run and
    ///     replayed from the cache on a hit — plus the merge notes the last <see cref="ExtractAsync" />
    ///     produced (empty before it runs). Read per call rather than captured, so a verb that renders after
    ///     extracting sees the notes and one that gates before it does not have to wait for them.
    /// </summary>
    public WorkspaceDiagnostics Diagnostics => loadDiagnostics with { MergeNotes = mergeNotes };

    /// <summary>
    ///     Absolute path to the discovered <c>.sln</c>/<c>.slnx</c>, or to the <c>.slnf</c> filtering one.
    /// </summary>
    public string SolutionPath { get; }

    /// <summary>
    ///     The solution's file name — what every document names the run's subject by, and what a narrowing
    ///     notice names the filter by. Machine-independent, which is why no document carries
    ///     <see cref="SolutionPath" /> itself. Under a <c>.slnf</c> this is the filter's own name, unlike
    ///     <see cref="SolutionDirectory" />: the filter is what the run was pointed at, and naming it is how a
    ///     reader learns the answer covers a lens rather than the whole solution.
    /// </summary>
    // Assigned once in the ctor beside SolutionDirectory, for the same reason: every rendering verb reads it,
    // and two of them read it twice.
    public string SolutionName { get; }

    /// <summary>
    ///     The solution directory — baselines, render targets, diff resolution, <c>context --path</c> and every
    ///     relativized evidence path anchor here. Under a <c>.slnf</c> that is the directory of the solution the
    ///     filter <em>references</em>, not the filter's own: a filter is a lens on a solution, so a run through
    ///     one must resolve the same conventions and write to the same places as a run over the solution itself.
    /// </summary>
    // Assigned once in the ctor, never computed per read: under a .slnf the computation costs a file
    // read, and the property is read 2-16 times a run.
    public string SolutionDirectory { get; }

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
    public static Task<CodebaseSource> CreateWithSpecAsync(
        ISolutionSource source,
        IEnvironment environment,
        string? solution,
        string? spec,
        string workingDirectory,
        bool noCache,
        CancellationToken ct)
    {
        string solutionPath = ModelPipeline.DiscoverSolution(solution, workingDirectory);
        ExtractionCacheStore? store = noCache ? null : TryCreateStore(solutionPath, environment);
        return CreateWithSpecCoreAsync(source, solutionPath, spec, store, ct);
    }

    /// <summary>
    ///     Discovers the solution and prepares a spec-ful source for the verbs that never front the persisted
    ///     extraction cache — <c>render</c>, <c>baseline</c>, <c>explain</c> and the <c>arch_context</c> tool.
    ///     Identical to the cache-aware overload with <c>noCache: true</c>: nothing is read from
    ///     <c>cache.json</c> and nothing written to it, so what these runs touch on disk is what a plain cold
    ///     run touches. They still acquire through the same seam, so a host that keeps a warm session serves
    ///     them its model (<see cref="SolutionHandle.WarmCodebase" />) rather than a fresh walk per call.
    /// </summary>
    /// <remarks>
    ///     The persisted cache is deliberately left to the three verbs that ratchet on it. These four either
    ///     write files from the model (<c>render</c>, <c>baseline</c>) or answer a single lookup out of it
    ///     (<c>explain</c>, <c>arch_context</c>), and none of them is the caller a cache-invalidation bug
    ///     should first be discovered by.
    /// </remarks>
    public static Task<CodebaseSource> CreateWithSpecAsync(
        ISolutionSource source, string? solution, string? spec, string workingDirectory, CancellationToken ct)
    {
        string solutionPath = ModelPipeline.DiscoverSolution(solution, workingDirectory);
        return CreateWithSpecCoreAsync(source, solutionPath, spec, store: null, ct);
    }

    // The shared tail of both spec-ful entry points, from the point the cache store — the one thing they
    // decide differently — is settled. A null store is a Disabled run: nothing read from cache.json and
    // nothing written to it, which is what --no-cache and the four cache-free verbs both mean.
    private static async Task<CodebaseSource> CreateWithSpecCoreAsync(
        ISolutionSource source, string solutionPath, string? spec, ExtractionCacheStore? store, CancellationToken ct)
    {
        string normalized = NormalizeSpecArgument(spec);

        if (store is null)
            return await CreateColdWithSpecAsync(
                source, solutionPath, spec, normalized, null, CacheReadResult.Miss(),
                CodebaseSourceOutcome.Disabled, ct);

        CacheReadResult read = store.ReadAndValidate(ct);
        if (read.Outcome == CacheOutcome.Hit
            && ResolveSpecOnHit(spec, read.SpecResolutions) is { } hitResolution)
        {
            ArchitectureModel hitModel = source.LoadSpecModel(hitResolution.DllPath);
            return new CodebaseSource(
                CodebaseSourceOutcome.Hit, solutionPath, read.LoadDiagnostics, hitModel, hitResolution, null,
                store, read, normalized);
        }

        // A miss, a partial, or a hit whose spec was not recorded: acquire the workspace and resolve cold.
        CodebaseSourceOutcome coldOutcome = ColdOutcomeFor(read.Outcome);
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
                CodebaseSourceOutcome.Hit, solutionPath, read.LoadDiagnostics, null, null, null, store, read, "");

        CodebaseSourceOutcome coldOutcome = ColdOutcomeFor(read.Outcome);
        return await CreateColdSpeclessAsync(source, solutionPath, store, read, coldOutcome, ct);
    }

    /// <summary>
    ///     Produces the codebase model, excluding <paramref name="excludeProjectNames" /> (the spec project
    ///     and its private plumbing, or empty for <c>graph</c>). On a hit the cached fragments are merged
    ///     directly; otherwise the workspace is fingerprinted, the needed projects are extracted (the clean
    ///     ones reused on a partial), the subset is merged for the model, and the whole fragment set is
    ///     written back best-effort.
    /// </summary>
    public async Task<CodebaseModel> ExtractAsync(IReadOnlyCollection<string> excludeProjectNames, CancellationToken ct)
    {
        CodebaseModel codebase = await ExtractCoreAsync(excludeProjectNames, ct);
        // The advisory merge notes are a function of the merged fragments, so every path (hit, warm, cold)
        // regenerates them through FragmentMerger — the cache stores fragments, never notes. Captured here,
        // off the one exit, so Diagnostics can surface them beside the workspace-load failures.
        mergeNotes = codebase.MergeNotes;
        return codebase;
    }

    private async Task<CodebaseModel> ExtractCoreAsync(
        IReadOnlyCollection<string> excludeProjectNames, CancellationToken ct)
    {
        if (Outcome == CodebaseSourceOutcome.Hit)
            return FragmentMerger.Merge(FragmentMerger.Retain(cacheRead.ReusableFragments, excludeProjectNames));

        Solution solution = handle!.Solution;

        if (store is null) // Disabled: no persisted cache — either a pure cold walk or the warm incremental path.
        {
            if (handle.WarmCodebase is { } warmCodebase)
            {
                // The warm MCP path: the session-scoped store reuses clean projects' fragments, re-walks only
                // the dirty ∪ dependent set, and terminates in the same FragmentMerger every path uses —
                // memoized against the fragment set and this exclusion, so a call that re-walked nothing gets
                // the model unchanged. Exclusion goes down as an argument because it is applied at merge
                // time, so one store serves every tool whatever it drops; the store's re-extraction set
                // becomes this source's observable so the runner counters keep meaning.
                SessionCodebase warm = await warmCodebase(excludeProjectNames, declaredMembers.Value, ct);
                reExtractedProjects = new HashSet<string>(warm.ReExtractedProjects, StringComparer.Ordinal);
                return warm.Model;
            }

            return await CodebaseExtractor.ExtractFromSolutionAsync(
                solution, excludeProjectNames, handle.TargetFrameworks, declaredMembers.Value, ct);
        }

        // Fingerprint before extraction so a mid-run edit is caught by the store's re-stat at write time.
        CacheFingerprint? fingerprint = TryCaptureFingerprint(solution, ct);

        List<CodebaseFragment> allFragments = await ExtractAllFragmentsAsync(solution, ct);
        CodebaseModel merged = FragmentMerger.Merge(FragmentMerger.Retain(allFragments, excludeProjectNames));

        if (fingerprint is not null)
            TryWrite(fingerprint, allFragments, ct);

        return merged;
    }

    // On a partial, extract only the dirty projects and reuse the clean fragments; on a miss, extract them
    // all. Either way the result is the full fragment set in ordinal project order, so the merge and the
    // write-back order match a cold run exactly.
    private async Task<List<CodebaseFragment>> ExtractAllFragmentsAsync(Solution solution, CancellationToken ct)
    {
        if (Outcome == CodebaseSourceOutcome.Partial)
        {
            IReadOnlyList<CodebaseFragment> reExtracted = await CodebaseExtractor.ExtractFragmentsAsync(
                solution, cacheRead.DirtyProjects, handle!.TargetFrameworks, declaredMembers.Value, ct);
            reExtractedProjects = new HashSet<string>(cacheRead.DirtyProjects, StringComparer.Ordinal);
            return cacheRead.ReusableFragments
                .Concat(reExtracted)
                .OrderBy(f => f.ProjectName, StringComparer.Ordinal)
                .ToList();
        }

        List<CodebaseFragment> all =
        [
            .. await CodebaseExtractor.ExtractFragmentsAsync(
                solution, null, handle!.TargetFrameworks, declaredMembers.Value, ct)
        ];
        reExtractedProjects = all.Select(f => f.ProjectName).ToHashSet(StringComparer.Ordinal);
        return all;
    }

    // ── construction helpers ──────────────────────────────────────────────────────────────────────────────

    private static async Task<CodebaseSource> CreateColdWithSpecAsync(
        ISolutionSource source, string solutionPath, string? spec, string normalizedSpec,
        ExtractionCacheStore? store, CacheReadResult cacheRead, CodebaseSourceOutcome outcome, CancellationToken ct)
    {
        SolutionHandle handle = await source.AcquireAsync(solutionPath, ct);
        try
        {
            // Forced here and travelling on as the same Lazy, so what resolution read is what extraction gets.
            var declaredMembers = new Lazy<IReadOnlySet<string>?>(() => SpecExclusion.TryReadDeclaredMembers(solutionPath));
            SpecResolution resolution = SpecResolver.Resolve(
                handle.Solution, declaredMembers.Value, spec, handle.LoadDiagnostics);
            ArchitectureModel model = source.LoadSpecModel(resolution.DllPath);
            return new CodebaseSource(
                outcome, solutionPath, handle.LoadDiagnostics, model, resolution, handle, store, cacheRead,
                normalizedSpec, declaredMembers);
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
        SolutionHandle handle = await source.AcquireAsync(solutionPath, ct);
        return new CodebaseSource(
            outcome, solutionPath, handle.LoadDiagnostics, null, null, handle, store, cacheRead, "");
    }

    // ── spec replay on a hit ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    ///     Resolves the spec on a cache hit without a workspace, or returns null when the cold path is needed.
    ///     An explicit DLL resolves directly (a missing one throws the same loud error a cold run would); a
    ///     convention or csproj spec replays its recorded resolution, re-running the built-output check so the
    ///     bounded search from the output root and its error text match cold; a spec with no matching record
    ///     returns null so the caller reloads the workspace.
    /// </summary>
    /// <remarks>
    ///     The recorded intermediate assembly path is replayed rather than left null, because that is what
    ///     makes the search's refusal of an intermediate result identical on both paths: without it a hit
    ///     would answer with the <c>obj</c>-side assembly a cold run refuses.
    /// </remarks>
    internal static SpecResolution? ResolveSpecOnHit(string? spec, IReadOnlyList<SpecResolutionRecord> records)
    {
        if (SpecResolver.TryResolveWithoutSolution(spec) is { } dllResolution) return dllResolution;

        string normalized = NormalizeSpecArgument(spec);
        SpecResolutionRecord? record = records.FirstOrDefault(r => string.Equals(r.NormalizedSpecArgument, normalized, StringComparison.Ordinal));
        if (record is null) return null;

        string dll = SpecResolver.RequireBuiltOutput(
            record.SpecProjectName ?? normalized, record.OutputFilePaths, record.IntermediateAssemblyPath);
        return new SpecResolution(
            dll, record.SpecProjectName, record.ExcludeProjectNames, record.OutputFilePaths,
            record.IntermediateAssemblyPath);
    }

    // ── cache write ───────────────────────────────────────────────────────────────────────────────────────

    private CacheFingerprint? TryCaptureFingerprint(Solution solution, CancellationToken ct)
    {
        try
        {
            IReadOnlyList<ProjectInputs> inputs = SolutionCacheInputs.Collect(solution);
            return store!.CaptureFingerprint(inputs, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null; // could not fingerprint ⇒ skip the write and keep the correct, freshly extracted model
        }
    }

    private void TryWrite(CacheFingerprint fingerprint, IReadOnlyList<CodebaseFragment> allFragments, CancellationToken ct)
    {
        try
        {
            IReadOnlyList<SpecResolutionRecord> records = BuildWriteSpecRecords();
            store!.Write(fingerprint, new ExtractionResult(allFragments, records, loadDiagnostics), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The cache is disposable derived data: a write failure must never change the run's result.
        }
    }

    // The spec record to persist for this run, unioned onto the manifest's existing records (replacing any
    // with the same normalized argument). Only a solution-member spec (convention/csproj — it names a spec
    // project) is recorded, since that is the resolution a hit cannot replay without one; an explicit DLL
    // resolves on a hit with no record, and graph has no spec at all. The whole excluded set rides along:
    // the hit path has no workspace to re-walk the spec project's ProjectReference closure with.
    private IReadOnlyList<SpecResolutionRecord> BuildWriteSpecRecords()
    {
        IReadOnlyList<SpecResolutionRecord> existing = cacheRead.SpecResolutions;
        if (resolution?.SpecProjectName is not { } specProjectName) return existing;

        // The built-output inputs come off the resolution that consumed them rather than being re-derived
        // here. Re-deriving could only group the spec project's Roslyn projects by NAME, while resolution
        // groups them by canonicalized project file — the distinction that keeps two same-named csprojs from
        // recording each other's outputs. Both slots are non-null on this branch: naming a spec project is
        // exactly what the solution-member resolution does, and that is the branch that fills them.
        IReadOnlyList<string> outputFilePaths = resolution.OutputFilePaths ?? [];

        var record = new SpecResolutionRecord(
            normalizedSpecArgument, specProjectName, [.. resolution.ExcludeProjectNames], outputFilePaths,
            resolution.IntermediateAssemblyPath);
        return existing
            .Where(r => !string.Equals(r.NormalizedSpecArgument, normalizedSpecArgument, StringComparison.Ordinal))
            .Append(record)
            .ToList();
    }

    // ── small helpers ─────────────────────────────────────────────────────────────────────────────────────

    // What a run that has to open a workspace calls itself: a partial read keeps its clean fragments and
    // re-extracts only the dirty ones, and every other read (a miss, or a hit the spec could not replay)
    // extracts the lot.
    private static CodebaseSourceOutcome ColdOutcomeFor(CacheOutcome outcome)
    {
        return outcome == CacheOutcome.Partial ? CodebaseSourceOutcome.Partial : CodebaseSourceOutcome.Miss;
    }

    private static string NormalizeSpecArgument(string? spec)
    {
        return string.IsNullOrWhiteSpace(spec) ? "" : Path.GetFullPath(spec);
    }

    private static ExtractionCacheStore? TryCreateStore(string solutionPath, IEnvironment environment)
    {
        try
        {
            return new ExtractionCacheStore(solutionPath, environment.CacheRootOverride());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null; // no resolvable cache location ⇒ run cold, no read and no write
        }
    }
}
