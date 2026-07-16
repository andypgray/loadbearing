using Microsoft.CodeAnalysis;
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
internal readonly record struct SessionFragmentSet(
    IReadOnlyList<CodebaseFragment> Fragments,
    IReadOnlySet<string> ReExtractedProjects);

/// <summary>
///     A session-lifetime, incremental fragment store for the warm MCP server (Phase 12 D2). It holds the
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
/// </remarks>
internal sealed class SessionFragmentStore
{
    private static readonly IReadOnlySet<string> NoProjects = new HashSet<string>(StringComparer.Ordinal);

    // Keyed by project name; a LIST per name because a multi-target-framework project surfaces as several
    // same-name Projects and therefore several fragments. Each list preserves the cold within-name order
    // (ExtractFragmentsAsync's stable order), which OrderedFragments relies on to reproduce cold order.
    private readonly Dictionary<string, List<CodebaseFragment>> fragmentsByProject = new(StringComparer.Ordinal);

    private readonly SemaphoreSlim gate = new(1, 1);

    // The generation the stored fragments were extracted at. -1 means nothing extracted yet; a loaded
    // snapshot's generation is always >= 1, so the first call always mismatches and full-extracts.
    private long extractedGeneration = -1;

    // The edit-version map the stored fragments were extracted at, for same-generation dirty diffing.
    private IReadOnlyDictionary<string, int> extractedVersions = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>
    ///     The project names the last <see cref="GetFragmentsAsync" /> re-walked: every C# project on a full
    ///     walk, the dirty ∪ dependents set on an incremental one, empty on a pure steady-state call.
    ///     Internal test observable (the D2 acceptance — "a warm re-extract walks only projects whose
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
    internal async Task<SessionFragmentSet> GetFragmentsAsync(WorkspaceSnapshot snapshot, CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return snapshot.Generation != extractedGeneration
                ? await FullWalkAsync(snapshot, ct).ConfigureAwait(false)
                : await IncrementalWalkAsync(snapshot, ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<SessionFragmentSet> FullWalkAsync(WorkspaceSnapshot snapshot, CancellationToken ct)
    {
        var fragments =
            await CodebaseExtractor.ExtractFragmentsAsync(snapshot.Solution, null, ct).ConfigureAwait(false);

        fragmentsByProject.Clear();
        Index(fragments);
        extractedGeneration = snapshot.Generation;
        extractedVersions = Copy(snapshot.ProjectEditVersions);
        FullWalkCount++;
        LastReExtractedProjects = fragments.Select(f => f.ProjectName).ToHashSet(StringComparer.Ordinal);
        return new SessionFragmentSet(OrderedFragments(), LastReExtractedProjects);
    }

    private async Task<SessionFragmentSet> IncrementalWalkAsync(WorkspaceSnapshot snapshot, CancellationToken ct)
    {
        var dirty = ExpandToDependents(ContentDirtyProjects(snapshot), snapshot.Solution);
        if (dirty.Count > 0)
        {
            var reExtracted =
                await CodebaseExtractor.ExtractFragmentsAsync(snapshot.Solution, dirty, ct).ConfigureAwait(false);

            // Replace only the re-extracted names' lists; the clean projects' fragments ride through untouched.
            foreach (string name in dirty) fragmentsByProject.Remove(name);
            Index(reExtracted);
        }

        extractedVersions = Copy(snapshot.ProjectEditVersions);
        LastReExtractedProjects = dirty;
        return new SessionFragmentSet(OrderedFragments(), dirty);
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

        var dependentsByName = BuildReverseReferenceGraph(solution);
        var result = new HashSet<string>(seed, StringComparer.Ordinal);
        var queue = new Queue<string>(seed);
        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            if (!dependentsByName.TryGetValue(current, out var dependents)) continue;

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

                if (!dependentsByName.TryGetValue(referenced, out var dependents))
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
            if (!fragmentsByProject.TryGetValue(fragment.ProjectName, out var list))
            {
                list = [];
                fragmentsByProject[fragment.ProjectName] = list;
            }

            list.Add(fragment);
        }
    }

    // The whole stored set in ordinal-project-name order. OrderBy is a stable sort, so within a name the
    // fragments keep their stored (cold) order — reproducing a cold run's fragment order exactly, and with
    // it the merged model byte for byte.
    private IReadOnlyList<CodebaseFragment> OrderedFragments()
    {
        return fragmentsByProject.Values
            .SelectMany(list => list)
            .OrderBy(f => f.ProjectName, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyDictionary<string, int> Copy(IReadOnlyDictionary<string, int> versions)
    {
        return new Dictionary<string, int>(versions, StringComparer.Ordinal);
    }
}