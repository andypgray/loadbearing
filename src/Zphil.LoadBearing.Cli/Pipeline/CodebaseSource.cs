using Microsoft.CodeAnalysis;
using Zphil.LoadBearing.Cli.Mcp.Infrastructure;
using Zphil.LoadBearing.Cli.SpecLoading;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Hosting;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Caching;
using Zphil.LoadBearing.Roslyn.Diagnostics;
using Zphil.LoadBearing.Roslyn.Extraction;
using Zphil.LoadBearing.Roslyn.Solutions;

namespace Zphil.LoadBearing.Cli.Pipeline;

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
///         <b>One walk per source, however many models a run asks for.</b> The fragments are walked at most
///         once for the source's lifetime and the merged models are memoized against them, keyed by
///         exclusion — the same two tiers <see cref="SessionFragmentStore" /> holds for the warm path, and
///         through its key, so the two cannot disagree about when a merge repeats. The split matters because
///         the costs are three orders of magnitude apart: <c>render --diagram</c> asks for the spec's
///         exclusions to place its cards and then for none at all to draw the survey, which before this was
///         two full walks of the solution for one run.
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
///             Which verbs front the persisted cache is a policy, and it sorts them by what they read
///             absence as.
///         </b>
///         A verb that reads <em>presence</em> — a violation it saw, a rule it found, a card it can place —
///         may front it, and <c>check</c>, <c>status</c>, <c>graph</c>, <c>render</c>, <c>explain</c> and
///         <c>baseline --add</c> all do. <c>baseline --init</c> and <c>baseline --accept-reductions</c> read
///         <em>absence as evidence</em> — "not in the model" becomes "no longer happening", written into a
///         file that outlives the run — so they force a cache-free extraction: a stale hit is a third route
///         to the smaller-than-real model the incomplete-model and narrowing gates already refuse those two
///         modes on, and the only one of the three that raises no diagnostic either gate can see. There is
///         one spec-ful entry point rather than a cache-free overload beside it, so every caller spells its
///         policy as the <c>noCache</c> argument instead of inheriting one from the ladder it was written
///         on.
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

    // The models merged from that fragment set, keyed by exclusion. Two tiers because the two costs are
    // three orders of magnitude apart: the walk above measured ~85 s on a 35-project solution, while a
    // merge is CPU over fragments already in hand. `render --diagram` is the caller that
    // proves the split — one walk for the whole run, then a second model over a different exclusion, because
    // the survey fence draws every project while the cards respect the spec's exclusions. Plain, unlocked
    // collections: a source belongs to one run and its ExtractAsync is never re-entered concurrently, unlike
    // the session-lifetime store the warm path uses.
    private readonly Dictionary<string, CodebaseModel> mergedByExclusion = new(StringComparer.Ordinal);

    private readonly ArchitectureModel? model;
    private readonly string normalizedSpecArgument;
    private readonly SpecResolution? resolution;
    private readonly ExtractionCacheStore? store;

    // The fragment set this source acquired, held for its lifetime — walked from the workspace, or taken
    // whole from a cache hit. Null until the first ExtractAsync, and written exactly once after it.
    private IReadOnlyList<CodebaseFragment>? fragments;

    // The advisory merge notes the last ExtractAsync produced (same-FQN cross-project conflation),
    // regenerated from the fragments on every path — a cache hit re-merges, so these need no persistence.
    // Empty until ExtractAsync runs, which is why Diagnostics is composed per read rather than captured.
    private IReadOnlyList<string> mergeNotes = [];

    // The other half of the same read: which projects arrived as several compilations. Captured beside the
    // notes because it comes off the same merged model and must not be able to disagree with them.
    private IReadOnlyList<MultiTargetedProject> multiTargetedProjects = [];

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
    ///     replayed from the cache on a hit — plus the two facts the last <see cref="ExtractAsync" /> produced,
    ///     its merge notes and its multi-targeted projects (both empty before it runs). Read per call rather
    ///     than captured, so a verb that renders after extracting sees them and one that gates before it does
    ///     not have to wait for them.
    /// </summary>
    public WorkspaceDiagnostics Diagnostics =>
        loadDiagnostics with { MergeNotes = mergeNotes, MultiTargetedProjects = multiTargetedProjects };

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

    /// <summary>
    ///     How many times this source walked the workspace for fragments — at most once for its whole
    ///     lifetime, however many exclusion sets its callers ask for, and zero on a cache hit or the warm
    ///     path (neither walks). Internal test observable; never printed.
    /// </summary>
    internal int ExtractionCount { get; private set; }

    /// <summary>Disposes the owned cold workspace; a no-op on a cache hit (which owns none).</summary>
    public void Dispose()
    {
        handle?.Dispose();
    }

    /// <summary>
    ///     Discovers the solution and prepares the spec-ful source every verb that consumes a spec acquires
    ///     through: on a cache hit the spec is replayed and the model loaded with no workspace; otherwise a
    ///     workspace is acquired and the spec resolved against it. Discovery, spec-resolution, and spec-load
    ///     failures surface exactly as a cold run raises them.
    /// </summary>
    /// <param name="source">The seam a run that needs a workspace acquires one through.</param>
    /// <param name="environment">
    ///     The seam the cache root is read through. Null means real process state, exactly as it does for the
    ///     runners and the gate — and it is never read at all under <paramref name="noCache" />, which is what
    ///     lets a caller whose policy is never to front the cache pass none.
    /// </param>
    /// <param name="solution">The positional solution argument (a file, a directory, or null for cwd walk-up).</param>
    /// <param name="spec">The <c>--spec</c> value, or null for the convention.</param>
    /// <param name="workingDirectory">The directory solution discovery walks up from.</param>
    /// <param name="noCache">
    ///     This caller's cache policy, spelled here rather than inherited: <c>true</c> reads nothing from
    ///     <c>cache.json</c> and writes nothing to it, so the run touches what a plain cold run touches. It
    ///     still acquires through the same seam either way, so a host keeping a warm session serves it that
    ///     session's model (<see cref="SolutionHandle.WarmCodebase" />) rather than a fresh walk per call.
    /// </param>
    /// <param name="ct">The run's cancellation token.</param>
    public static Task<CodebaseSource> CreateWithSpecAsync(
        ISolutionSource source,
        IEnvironment? environment,
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
    /// <remarks>
    ///     Callable more than once per source, and cheap after the first: the walk (and, with a store, the
    ///     fingerprint and the write-back) happen on the first call alone, and a repeat with an exclusion set
    ///     already merged hands back that model rather than rebuilding it. A repeat with a <em>different</em>
    ///     exclusion set still merges — it is a different model — but off the fragments already in hand.
    ///     The two facts below are re-read per call regardless, because they legitimately differ per
    ///     exclusion: which same-FQN types conflated, and which projects arrived multi-targeted.
    /// </remarks>
    public async Task<CodebaseModel> ExtractAsync(IReadOnlyCollection<string> excludeProjectNames, CancellationToken ct)
    {
        CodebaseModel codebase = await ExtractCoreAsync(excludeProjectNames, ct);
        // The advisory merge notes are a function of the merged fragments, so every path (hit, warm, cold)
        // regenerates them through FragmentMerger — the cache stores fragments, never notes. Captured here,
        // off the one exit, so Diagnostics can surface them beside the workspace-load failures.
        mergeNotes = codebase.MergeNotes;
        // The other half of the same read, off the shared projection rather than one of this file's own: the
        // slot's contract is that it and the notes come from one merge, and an owner beside the record is
        // what lets every composer holding a model keep that promise the same way.
        multiTargetedProjects = MultiTargetedProjects.Of(codebase);
        return codebase;
    }

    private async Task<CodebaseModel> ExtractCoreAsync(
        IReadOnlyCollection<string> excludeProjectNames, CancellationToken ct)
    {
        if (WarmCodebaseProducer() is { } warmCodebase)
        {
            // The warm MCP path: the session-scoped store reuses clean projects' fragments, re-walks only
            // the dirty ∪ dependent set, and terminates in the same FragmentMerger every path uses —
            // memoized against the fragment set and this exclusion, so a call that re-walked nothing gets
            // the model unchanged. Exclusion goes down as an argument because it is applied at merge
            // time, so one store serves every tool whatever it drops; the store's re-extraction set
            // becomes this source's observable so the runner counters keep meaning.
            //
            // It returns above this source's own memo rather than through it, deliberately: the store must
            // be re-consulted per call so an edit between two calls is re-walked, and a memo here would
            // answer the second call from the first call's model and strand the edit.
            SessionCodebase warm = await warmCodebase(excludeProjectNames, declaredMembers.Value, ct);
            reExtractedProjects = new HashSet<string>(warm.ReExtractedProjects, StringComparer.Ordinal);
            return warm.Model;
        }

        // One walk per source, then one merge per distinct exclusion set over the fragments it produced.
        // `render --diagram` is why: it asks for the spec's exclusions to place the cards and then for none
        // at all to draw the survey, and before this it paid the whole walk twice for the difference.
        fragments ??= await WalkFragmentsAsync(ct);
        return MergedFor(fragments, excludeProjectNames);
    }

    // The warm path's producer when this run takes it: a cold-loaded handle carrying a session codebase, and
    // no persisted store to prefer over it. Null on a hit (which owns no handle) and whenever a store is
    // live, since the store path serves every caller from the fragments it also writes back.
    private Func<IReadOnlyCollection<string>, IReadOnlySet<string>?, CancellationToken, Task<SessionCodebase>>?
        WarmCodebaseProducer()
    {
        if (Outcome == CodebaseSourceOutcome.Hit || store is not null) return null;

        return handle!.WarmCodebase;
    }

    // The fragment set this source merges from, acquired once. A hit takes the cached set whole and walks
    // nothing; every other path opens the workspace it already holds.
    private async Task<IReadOnlyList<CodebaseFragment>> WalkFragmentsAsync(CancellationToken ct)
    {
        if (Outcome == CodebaseSourceOutcome.Hit) return cacheRead.ReusableFragments;

        ExtractionCount++;
        Solution solution = handle!.Solution;

        if (store is null)
            // Disabled and cold: walk every project and let the merge above drop what each caller excludes.
            // That is byte-identical to excluding at collection — FragmentMerger.Retain is an order-preserving
            // filter on ProjectName, over a list CollectInputsAsync has already ordered — and it is what lets
            // one walk serve two exclusion sets. reExtractedProjects stays empty, as it always has on a
            // disabled run: the field says which projects the *cache* made this run re-extract.
            return await CodebaseExtractor.ExtractFragmentsAsync(
                solution, null, handle.TargetFrameworks, declaredMembers.Value, ct);

        // Fingerprint before extraction so a mid-run edit is caught by the store's re-stat at write time.
        // Both this and the write-back sit on the one-walk path, so a second ExtractAsync re-fingerprints
        // nothing and re-writes nothing.
        CacheFingerprint? fingerprint = TryCaptureFingerprint(solution, ct);

        List<CodebaseFragment> allFragments = await ExtractAllFragmentsAsync(solution, ct);
        if (fingerprint is not null)
            TryWrite(fingerprint, allFragments, ct);

        return allFragments;
    }

    // The merged model for one exclusion set, memoized against the walked fragments. Keyed through the
    // merger's own ExclusionKey, so this memo and the warm session store's cannot disagree about when two
    // callers are asking for the same merge. Sharing the instance is safe for the reason it is there: a
    // merged model is read-only once built, and every consumer — the checker, the renderers, the
    // summarizer — reads.
    private CodebaseModel MergedFor(
        IReadOnlyList<CodebaseFragment> walked, IReadOnlyCollection<string> excludeProjectNames)
    {
        string key = FragmentMerger.ExclusionKey(excludeProjectNames);
        if (mergedByExclusion.TryGetValue(key, out CodebaseModel? memoized)) return memoized;

        CodebaseModel merged = FragmentMerger.Merge(FragmentMerger.Retain(walked, excludeProjectNames));
        mergedByExclusion[key] = merged;
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
            SessionSpecResolution resolved = ResolveSpec(handle, solutionPath, spec, normalizedSpec);
            ArchitectureModel model = source.LoadSpecModel(resolved.Resolution.DllPath);
            // The membership set travels on as the one resolution read (or replayed), so what resolution
            // saw is what extraction gets — already computed, hence the value-taking Lazy.
            return new CodebaseSource(
                outcome, solutionPath, handle.LoadDiagnostics, model, resolved.Resolution, handle, store, cacheRead,
                normalizedSpec, new Lazy<IReadOnlySet<string>?>(resolved.DeclaredMembers));
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    /// <summary>
    ///     Resolves the spec against the acquired handle, through the session's memo where the handle
    ///     carries one (<see cref="SolutionHandle.WarmSpecResolution" />) and cold where it does not.
    /// </summary>
    /// <remarks>
    ///     A prebuilt-DLL <c>--spec</c> is answered first, before the memo is consulted at all — exactly as
    ///     <see cref="ResolveSpecOnHit" /> answers it first. That branch needs no workspace and no walk, so
    ///     there is nothing for a cache to spare; and replaying it would refuse in the built-output search's
    ///     words, which name a spec project a DLL spec does not have.
    /// </remarks>
    private static SessionSpecResolution ResolveSpec(
        SolutionHandle handle, string solutionPath, string? spec, string normalizedSpec)
    {
        if (SpecResolver.TryResolveWithoutSolution(spec) is { } dllResolution)
            return new SessionSpecResolution(dllResolution, SpecExclusion.TryReadDeclaredMembers(solutionPath));

        return handle.WarmSpecResolution is { } warm
            ? warm(normalizedSpec, ResolveFully)
            : ResolveFully();

        // The cold resolution, and the only reader of the solution's declared membership on this path: both
        // halves need the same set, so it is read once here and travels out with the resolution.
        SessionSpecResolution ResolveFully()
        {
            IReadOnlySet<string>? declaredMembers = SpecExclusion.TryReadDeclaredMembers(solutionPath);
            SpecResolution resolution = SpecResolver.Resolve(
                handle.Solution, declaredMembers, spec, handle.LoadDiagnostics);
            return new SessionSpecResolution(resolution, declaredMembers);
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
    ///     would answer with the <c>obj</c>-side assembly a cold run refuses. That whole live half runs
    ///     through <see cref="SpecResolver.Replay" />, the one owner the warm session's
    ///     <see cref="SpecResolutionCache" /> replays through too.
    /// </remarks>
    internal static SpecResolution? ResolveSpecOnHit(string? spec, IReadOnlyList<SpecResolutionRecord> records)
    {
        if (SpecResolver.TryResolveWithoutSolution(spec) is { } dllResolution) return dllResolution;

        string normalized = NormalizeSpecArgument(spec);
        SpecResolutionRecord? record = records.FirstOrDefault(r => string.Equals(r.NormalizedSpecArgument, normalized, StringComparison.Ordinal));
        if (record is null) return null;

        return SpecResolver.Replay(
            record.SpecProjectName, normalized, record.ExcludeProjectNames, record.OutputFilePaths,
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

    private static ExtractionCacheStore? TryCreateStore(string solutionPath, IEnvironment? environment)
    {
        try
        {
            return new ExtractionCacheStore(solutionPath, (environment ?? new SystemEnvironment()).CacheRootOverride());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null; // no resolvable cache location ⇒ run cold, no read and no write
        }
    }
}
