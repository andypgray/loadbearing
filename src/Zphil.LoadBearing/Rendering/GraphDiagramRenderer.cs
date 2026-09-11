using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     Draws a solution's projects and the references between them as a Mermaid flowchart: the body
///     <c>loadbearing render --diagram</c> writes into a file's managed block. Pure over a
///     <see cref="GraphSummary" />, so the drawing shows exactly what the <c>loadbearing graph</c>
///     survey reports. The output is always LF and carries no timestamp or tool version, so an unchanged
///     codebase re-renders to no diff at all.
/// </summary>
/// <remarks>
///     The drawing is deliberately plain: <c>flowchart LR</c> with quoted labels,
///     <c>accTitle</c> and <c>accDescr</c> for screen readers, and no <c>%%{init}%%</c> block,
///     <c>classDef</c> or colours, so it renders correctly for a reader in a light or a dark theme.
///     Structure carries the meaning: a solid <c>--&gt;</c> is a cross-project reference the code
///     actually makes, and a dotted <c>-.-&gt;</c> is a project reference that is declared and never
///     used. Labels carry no type or reference counts, so a committed drawing does not change every time
///     the code does; the counts are in the <c>loadbearing graph</c> survey, which nothing commits.
/// </remarks>
public static class GraphDiagramRenderer
{
    // Every node ID carries this prefix, which is what keeps a project name from ever being lexed as
    // something other than a node. Mermaid's flowchart grammar reserves bare words — end, graph, class,
    // subgraph and more — none of them anchored to the start of a statement, so a project of any of
    // those names would break the whole diagram rather than just its own node. Matching that list
    // instead would be correct only until the grammar grows a new word, and this renderer runs against
    // other people's solutions, where "graph" or "class" is a plausible project name. A prefix cannot
    // rot. It also makes the grammar's case sensitivity irrelevant rather than something to rely on,
    // and it closes a second hazard for free: the link rules read a leading o or x as an arrowhead,
    // and no prefixed ID can begin with either.
    private const string NodeIdPrefix = "p_";

    private const string EmptyScopeNodeId = NodeIdPrefix + "none";

    private const string EmptyScopeLabel = "(no projects in scope)";

    /// <summary>
    ///     The managed-block body for a diagram file: a provenance caption, then the fenced Mermaid diagram.
    ///     The caption sits outside the fence, so the fence stays paste-able into any Mermaid renderer.
    /// </summary>
    /// <param name="summary">The codebase survey to draw.</param>
    /// <param name="solutionName">The solution file's name, named in the caption and in the accessible title.</param>
    /// <param name="scope">
    ///     The project filter; null draws every project (<see cref="DiagramScope.Everything" />).
    /// </param>
    /// <remarks>
    ///     Only projects the solution declares are drawn, under any <paramref name="scope" />: a workspace
    ///     also loads whatever a project reference reaches, and an unfiltered drawing would otherwise put
    ///     those on a page captioned "Projects in this solution". The scope narrows within that set, so it
    ///     is a legibility knob rather than the thing keeping a foreign project out. A project whose
    ///     membership could not be read at all (<c>ProjectSummary.SolutionMember</c> is null) is drawn
    ///     rather than dropped, so an unparseable solution file degrades the drawing instead of emptying it.
    ///     To see the projects a drawing leaves out, run <c>loadbearing graph</c>.
    /// </remarks>
    public static string Block(GraphSummary summary, string solutionName, DiagramScope? scope = null)
    {
        Guard.NotNull(summary, nameof(summary));
        Guard.NotNullOrWhiteSpace(solutionName, nameof(solutionName));

        DiagramScope filter = scope ?? DiagramScope.Everything;
        List<ProjectSummary> projects = summary.Projects
            .Where(project => project.SolutionMember != false && filter.Includes(project.Name))
            .ToList();
        Dictionary<string, string> ids = NodeIds(projects);
        IReadOnlyList<string> nodeLines = NodeLines(projects, ids);
        List<string> edgeLines = EdgeLines(summary, projects, ids);
        IReadOnlyList<string> lines = MermaidText.Fence(
            $"Codebase survey: {solutionName}",
            "Projects in this solution and their cross-project references.",
            nodeLines,
            edgeLines);

        return ProvenanceLine(solutionName) + "\n\n" + string.Join("\n", lines);
    }

    // The provenance/warning caption. Names the solution rather than the spec: the survey is a property of
    // the codebase, so there is no spec to go and edit — the remedy is a re-render. That em-dash is the one
    // ARCHITECTURE.md's prose budget affords, which is why the law caption below it spells the same clause
    // with a full stop; see LawDiagramRenderer.Caption before adding a second one here.
    private static string ProvenanceLine(string solutionName)
    {
        return $"*Generated by `loadbearing render` from `{solutionName}` — " +
               "do not edit between the markers; re-render to update.*";
    }

    // One node per in-scope project, in the summary's ordinal order. An empty scope emits the (none)
    // placeholder rather than an empty diagram, so the artifact's shape stays stable — the same convention
    // the text survey's empty sections follow.
    private static IReadOnlyList<string> NodeLines(IReadOnlyList<ProjectSummary> projects, IReadOnlyDictionary<string, string> ids)
    {
        return projects.Count > 0
            ? projects.Select(project => $"    {ids[project.Name]}[\"{MermaidText.Label(project.Name)}\"]").ToList()
            : [$"    {EmptyScopeNodeId}[\"{EmptyScopeLabel}\"]"];
    }

    // Observed edges first (ordinal by source then target, the summary's own order), then the
    // declared-but-unobserved ones (ordinal by project, then by declared reference). An edge is drawn only
    // when both endpoints are nodes, so a reference out of scope — or out of the solution — leaves no
    // phantom behind.
    private static List<string> EdgeLines(
        GraphSummary summary, IReadOnlyList<ProjectSummary> projects, IReadOnlyDictionary<string, string> ids)
    {
        var observed = new HashSet<(string Source, string Target)>(
            summary.ProjectEdges.Select(edge => (edge.Source, edge.Target)));

        List<string> lines = summary.ProjectEdges
            .Where(edge => ids.ContainsKey(edge.Source) && ids.ContainsKey(edge.Target))
            .Select(edge => $"    {ids[edge.Source]} --> {ids[edge.Target]}")
            .ToList();

        lines.AddRange(projects
            .SelectMany(project => project.ProjectReferences.Select(reference => (Source: project.Name, Target: reference)))
            .Where(pair => ids.ContainsKey(pair.Target) && !observed.Contains(pair))
            .Select(pair => $"    {ids[pair.Source]} -.-> {ids[pair.Target]}"));

        return lines;
    }

    // Project name → Mermaid node ID: the prefix plus a deterministic slug, deduped with an ordinal suffix
    // so two names that slug alike (MyApp.Web and MyApp-Web) still get distinct nodes. Slugging, dedupe
    // and label escaping are MermaidText's, shared with the law fence in the same artifact. The map is
    // keyed by the name, because that is what an edge names at both ends.
    private static Dictionary<string, string> NodeIds(IReadOnlyList<ProjectSummary> projects)
    {
        List<string> names = projects.Select(project => project.Name).ToList();
        return MermaidText.IdMap(NodeIdPrefix, names, name => name, StringComparer.Ordinal);
    }
}
