using Microsoft.CodeAnalysis;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn.Caching;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     What one <see cref="SessionFragmentStore.GetFragmentsAsync" /> call produced: the full fragment set
///     for every C# project — in the ordinal-project-name order a cold run yields, so the downstream
///     <see cref="FragmentMerger" /> is byte-identical — plus the names of the projects this call actually
///     re-walked. The caller merges the fragments (dropping its own spec project) and surfaces
///     <see cref="ReExtractedProjects" /> as its re-extraction observable, so the CLI runner counters keep
///     their meaning on the warm path.
/// </summary>
/// <param name="Fragments">Every C# project's fragments, in cold order.</param>
/// <param name="ReExtractedProjects">The projects this call re-walked; empty in the steady state.</param>
/// <param name="Version">
///     The store's fragment-set version these fragments were taken at — bumped only when the stored
///     fragments actually change, so two sets sharing a version hold the identical fragment instances. It
///     is what <see cref="SessionFragmentStore.GetCodebaseAsync" /> memoizes the merge against.
/// </param>
internal readonly record struct SessionFragmentSet(
    IReadOnlyList<CodebaseFragment> Fragments,
    IReadOnlySet<string> ReExtractedProjects,
    long Version);

/// <summary>
///     What one <see cref="SessionFragmentStore.GetCodebaseAsync" /> call produced: the merged
///     <see cref="CodebaseModel" /> — shared, and possibly memoized from an earlier call — plus the names of
///     the projects that call re-walked, which the caller surfaces as its re-extraction observable.
/// </summary>
internal readonly record struct SessionCodebase(
    CodebaseModel Model,
    IReadOnlySet<string> ReExtractedProjects);

/// <summary>
///     A session-lifetime, incremental fragment store for the warm MCP server. It holds the
///     last-extracted <see cref="CodebaseFragment" />s keyed by project name and, on each
///     <see cref="GetFragmentsAsync" />, reuses the clean projects' fragments and re-extracts only the ones
///     whose bytes changed — expanded to their reference-graph dependents — before returning the whole set,
///     which the caller feeds to the one <see cref="FragmentMerger" /> every extraction path terminates in.
///     One store pairs with one <see cref="WorkspaceSession" /> for its whole lifetime (both DI singletons,
///     wired together in the MCP host).
/// </summary>
/// <remarks>
///     <para>
///         <b>Generation is the flush signal.</b> A <see cref="WorkspaceSnapshot" /> carries the session's
///         load <see cref="WorkspaceSnapshot.Generation" />, bumped on every full (re)load. A snapshot from a
///         new generation — or the very first call — flushes the store and re-extracts every C# project, so
///         the structural-reload case falls out of this one check with no special handling. Within a
///         generation, the snapshot's <see cref="WorkspaceSnapshot.ProjectEditVersions" /> pinpoints the
///         dirty projects. The store assumes every snapshot it is handed comes from its one paired session,
///         so a generation value is never aliased by a foreign session's — true by construction.
///     </para>
///     <para>
///         <b>Dirty ∪ reference-graph dependents.</b> A content change in project P dirties P plus every
///         project that transitively references P, because P's change can alter what a dependent compilation
///         sees as an external type (or flip a declared-vs-external classification at merge time) — the same
///         reason the persisted extraction cache propagates dirtiness up its Merkle keys. Re-extracting the
///         dependents is conservative but never wrong; reusing them could strand a stale fact.
///     </para>
///     <para>
///         <b>No exclusion here.</b> The store always holds and returns fragments for <em>every</em> C#
///         project, the spec project included, so one store serves all five tools whatever each excludes; the
///         spec-project drop happens at merge time in the caller (<c>CodebaseSource</c>).
///     </para>
///     <para>
///         <b>Concurrency.</b> Tool calls may overlap and extraction is asynchronous (a plain lock cannot
///         span the <c>await</c>), so a <see cref="SemaphoreSlim" /> serializes the whole compare-and-extract:
///         a read never races a re-extraction, and the returned set is always internally consistent.
///     </para>
///     <para>
///         <b>The merge is memoized too.</b> Reusing a clean project's fragments still left every call
///         rebuilding the whole <see cref="CodebaseModel" /> from them — every node, every hierarchy list,
///         every edge site set across the solution — which in the steady state (an armed per-edit check, or
///         two tools called back to back) is the same model built again from the same inputs.
///         <see cref="GetCodebaseAsync" /> therefore memoizes it against
///         <see cref="SessionFragmentSet.Version" /> and the caller's exclusion set. Handing back the same
///         instance is safe because a merged model is read-only once built: the merge is the only writer of
///         a <c>TypeNode</c>'s hierarchy and members, and every consumer — the checker, the renderers, the
///         summarizer — reads.
///     </para>
/// </remarks>
internal sealed class SessionFragmentStore
{
    private static readonly IReadOnlySet<string> NoProjects = new HashSet<string>(StringComparer.Ordinal);

    // Keyed by project name; a LIST per name because a multi-target-framework project surfaces as several
    // Projects under the one name the load boundary normalized them to, and therefore several fragments. Each
    // list preserves the cold within-name order — ExtractFragmentsAsync orders those fragments by target
    // framework — which OrderedFragments relies on to reproduce cold order.
    private readonly Dictionary<string, List<CodebaseFragment>> fragmentsByProject = new(StringComparer.Ordinal);

    private readonly SemaphoreSlim gate = new(1, 1);

    // The merged models for the fragment-set version in mergedVersion, keyed by exclusion set. Guarded by
    // its own lock rather than the extraction gate: a merge is CPU-bound and takes no snapshot, so it has no
    // business holding up the compare-and-extract.
    private readonly Dictionary<string, CodebaseModel> merged = new(StringComparer.Ordinal);

    private readonly Lock mergedGate = new();

    // The generation the stored fragments were extracted at. -1 means nothing extracted yet; a loaded
    // snapshot's generation is always >= 1, so the first call always mismatches and full-extracts.
    private long extractedGeneration = -1;

    // The edit-version map the stored fragments were extracted at, for same-generation dirty diffing.
    private IReadOnlyDictionary<string, int> extractedVersions = new Dictionary<string, int>(StringComparer.Ordinal);

    // Bumped only when the stored fragments actually change (every full walk, and an incremental walk that
    // re-extracted something), so a steady-state call reports the version its predecessor did — which is
    // exactly the condition under which the merged model can be handed back rather than rebuilt.
    private long fragmentSetVersion;

    // The fragment-set version the memo above holds models for; -1 until the first merge.
    private long mergedVersion = -1;

    // The ordered whole set for the version in orderedVersion, and that version (-1 until the first walk).
    // Same discipline as the merge memo one field up, for the same reason: a steady-state call re-sorted
    // every fragment in the solution to produce a list the merge memo then made no use of. Both fields are
    // only ever touched under the extraction gate, which the two walks and their one reader all hold.
    private IReadOnlyList<CodebaseFragment>? orderedFragments;
    private long orderedVersion = -1;

    /// <summary>
    ///     The project names the last <see cref="GetFragmentsAsync" /> re-walked: every C# project on a full
    ///     walk, the dirty ∪ dependents set on an incremental one, empty on a pure steady-state call.
    ///     Internal test observable ("a warm re-extract walks only projects whose
    ///     compilation identity changed"); never printed.
    /// </summary>
    internal IReadOnlySet<string> LastReExtractedProjects { get; private set; } = NoProjects;

    /// <summary>
    ///     The number of full flush-and-re-extract-everything walks — the first call plus every generation
    ///     change (structural reload). Internal test observable; never printed.
    /// </summary>
    internal long FullWalkCount { get; private set; }

    /// <summary>
    ///     Returns the full fragment set for <paramref name="snapshot" />, reusing everything it can. A new
    ///     generation (or the first call) flushes and re-extracts all C# projects; otherwise it re-extracts
    ///     exactly the projects whose edit version changed since the last call, expanded to their transitive
    ///     reference-graph dependents, and reuses the rest. The fragments come back in the ordinal-project-name
    ///     order a cold run produces, so the caller's <see cref="FragmentMerger" /> yields the identical model.
    /// </summary>
    /// <remarks>
    ///     <paramref name="declaredMembers" /> rides down to the extraction rather than being applied to the
    ///     stored set, and a reused fragment therefore keeps the membership it was extracted with. That is
    ///     safe for the one reason worth stating: membership can only change when the solution file does, and
    ///     the solution file is a structural input of the paired session's reconcile sweep — so the change
    ///     arrives as a new generation, which flushes this store before any of it is read again.
    /// </remarks>
    internal async Task<SessionFragmentSet> GetFragmentsAsync(
        WorkspaceSnapshot snapshot, IReadOnlySet<string>? declaredMembers, CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return snapshot.Generation != extractedGeneration
                ? await FullWalkAsync(snapshot, declaredMembers, ct).ConfigureAwait(false)
                : await IncrementalWalkAsync(snapshot, declaredMembers, ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    ///     The merged codebase for <paramref name="snapshot" />, dropping
    ///     <paramref name="excludeProjectNames" /> at merge time — <see cref="GetFragmentsAsync" /> followed
    ///     by the one <see cref="FragmentMerger" /> every extraction path terminates in, memoized so a call
    ///     that re-walked nothing hands back the model its predecessor built rather than rebuilding it from
    ///     the identical fragments.
    /// </summary>
    /// <remarks>
    ///     The memo is keyed by the fragment-set version <em>and</em> the exclusion set, so it can never
    ///     serve a model built from other inputs; a version change simply empties it. Two overlapping calls
    ///     can therefore both merge (the loser's entry is re-made), which costs a merge and never a wrong
    ///     model.
    /// </remarks>
    internal async Task<SessionCodebase> GetCodebaseAsync(
        WorkspaceSnapshot snapshot,
        IReadOnlyCollection<string> excludeProjectNames,
        IReadOnlySet<string>? declaredMembers,
        CancellationToken ct)
    {
        SessionFragmentSet set = await GetFragmentsAsync(snapshot, declaredMembers, ct).ConfigureAwait(false);
        return new SessionCodebase(Merge(set, excludeProjectNames), set.ReExtractedProjects);
    }

    private CodebaseModel Merge(SessionFragmentSet set, IReadOnlyCollection<string> excludeProjectNames)
    {
        string key = ExclusionKey(excludeProjectNames);
        lock (mergedGate)
        {
            if (mergedVersion != set.Version)
            {
                merged.Clear();
                mergedVersion = set.Version;
            }
            else if (merged.TryGetValue(key, out CodebaseModel? hit))
            {
                return hit;
            }

            CodebaseModel model = FragmentMerger.Merge(FragmentMerger.Retain(set.Fragments, excludeProjectNames));
            merged[key] = model;
            return model;
        }
    }

    // The exclusion set as one ordinal-stable string. Two callers excluding the same projects in a different
    // order are the same merge, so the key sorts; '\n' cannot occur in a project name.
    private static string ExclusionKey(IReadOnlyCollection<string> excludeProjectNames)
    {
        if (excludeProjectNames.Count == 0) return "";

        return string.Join("\n", excludeProjectNames.OrderBy(name => name, StringComparer.Ordinal));
    }

    private async Task<SessionFragmentSet> FullWalkAsync(
        WorkspaceSnapshot snapshot, IReadOnlySet<string>? declaredMembers, CancellationToken ct)
    {
        IReadOnlyList<CodebaseFragment> fragments = await CodebaseExtractor
            .ExtractFragmentsAsync(snapshot.Solution, null, snapshot.TargetFrameworks, declaredMembers, ct)
            .ConfigureAwait(false);

        fragmentsByProject.Clear();
        Index(fragments);
        extractedGeneration = snapshot.Generation;
        extractedVersions = Copy(snapshot.ProjectEditVersions);
        fragmentSetVersion++;
        FullWalkCount++;
        LastReExtractedProjects = fragments.Select(f => f.ProjectName).ToHashSet(StringComparer.Ordinal);
        return new SessionFragmentSet(OrderedFragments(), LastReExtractedProjects, fragmentSetVersion);
    }

    private async Task<SessionFragmentSet> IncrementalWalkAsync(
        WorkspaceSnapshot snapshot, IReadOnlySet<string>? declaredMembers, CancellationToken ct)
    {
        HashSet<string> dirty = ExpandToDependents(ContentDirtyProjects(snapshot), snapshot.Solution);
        if (dirty.Count > 0)
        {
            IReadOnlyList<CodebaseFragment> reExtracted = await CodebaseExtractor
                .ExtractFragmentsAsync(snapshot.Solution, dirty, snapshot.TargetFrameworks, declaredMembers, ct)
                .ConfigureAwait(false);

            // Replace only the re-extracted names' lists; the clean projects' fragments ride through untouched.
            foreach (string name in dirty) fragmentsByProject.Remove(name);
            Index(reExtracted);
            fragmentSetVersion++;
        }

        extractedVersions = Copy(snapshot.ProjectEditVersions);
        LastReExtractedProjects = dirty;
        return new SessionFragmentSet(OrderedFragments(), dirty, fragmentSetVersion);
    }

    // Projects whose edit version differs from the one the stored fragments were extracted at. Within a
    // generation both maps share a fixed key set (seeded at load), so a key is never actually missing; the
    // -1 default is a defensive "treat as dirty" for the impossible mismatch.
    private HashSet<string> ContentDirtyProjects(WorkspaceSnapshot snapshot)
    {
        var dirty = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string name, int version) in snapshot.ProjectEditVersions)
            if (extractedVersions.GetValueOrDefault(name, -1) != version)
                dirty.Add(name);
        return dirty;
    }

    // Expands a content-dirty set to include every project that transitively references a dirty one, over the
    // reverse edges of the snapshot's project-reference graph (name -> names that reference it), walked
    // breadth-first from the seed.
    private static HashSet<string> ExpandToDependents(HashSet<string> seed, Solution solution)
    {
        if (seed.Count == 0) return seed;

        Dictionary<string, HashSet<string>> dependentsByName = BuildReverseReferenceGraph(solution);
        var result = new HashSet<string>(seed, StringComparer.Ordinal);
        var queue = new Queue<string>(seed);
        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            if (!dependentsByName.TryGetValue(current, out HashSet<string>? dependents)) continue;

            foreach (string dependent in dependents)
                if (result.Add(dependent))
                    queue.Enqueue(dependent);
        }

        return result;
    }

    private static Dictionary<string, HashSet<string>> BuildReverseReferenceGraph(Solution solution)
    {
        var dependentsByName = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (Project project in solution.Projects)
        {
            if (project.Language != LanguageNames.CSharp) continue;

            foreach (ProjectReference reference in project.ProjectReferences)
            {
                if (solution.GetProject(reference.ProjectId)?.Name is not { } referenced) continue;

                if (!dependentsByName.TryGetValue(referenced, out HashSet<string>? dependents))
                {
                    dependents = new HashSet<string>(StringComparer.Ordinal);
                    dependentsByName[referenced] = dependents;
                }

                dependents.Add(project.Name);
            }
        }

        return dependentsByName;
    }

    private void Index(IReadOnlyList<CodebaseFragment> fragments)
    {
        foreach (CodebaseFragment fragment in fragments)
        {
            if (!fragmentsByProject.TryGetValue(fragment.ProjectName, out List<CodebaseFragment>? list))
            {
                list = [];
                fragmentsByProject[fragment.ProjectName] = list;
            }

            list.Add(fragment);
        }
    }

    // The whole stored set in ordinal-project-name order. OrderBy is a stable sort, so within a name the
    // fragments keep their stored (cold) order — reproducing a cold run's fragment order exactly, and with
    // it the merged model byte for byte. Held against the fragment-set version, which is bumped by exactly
    // the two walks that can change what is stored, so a call that re-walked nothing hands back the list its
    // predecessor built. Sharing the instance is safe for the reason the shared model is: a fragment is
    // immutable data and every consumer reads.
    private IReadOnlyList<CodebaseFragment> OrderedFragments()
    {
        if (orderedVersion == fragmentSetVersion && orderedFragments is { } cached) return cached;

        List<CodebaseFragment> ordered = fragmentsByProject.Values
            .SelectMany(list => list)
            .OrderBy(f => f.ProjectName, StringComparer.Ordinal)
            .ToList();

        orderedFragments = ordered;
        orderedVersion = fragmentSetVersion;
        return ordered;
    }

    private static IReadOnlyDictionary<string, int> Copy(IReadOnlyDictionary<string, int> versions)
    {
        return new Dictionary<string, int>(versions, StringComparer.Ordinal);
    }
}
