using System.Text;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     Composes the codebase survey as a Mermaid flowchart — the managed-block body behind
///     <c>render --diagram</c>. Pure over a <see cref="GraphSummary" />, so the same summary the
///     <c>graph</c> survey prints draws the diagram; output is LF-internal always and carries no timestamp
///     or tool version, so an unchanged codebase re-renders to a zero diff.
/// </summary>
/// <remarks>
///     House dialect, taken from a survey of committed Mermaid in respected repositories:
///     <c>flowchart LR</c> with quoted labels, <c>accTitle</c>/<c>accDescr</c> for screen readers, and no
///     <c>%%{init}%%</c> block, <c>classDef</c>, or colours at all — theme-neutral source renders correctly
///     in a light or a dark reader by construction. Structure carries the meaning: a solid <c>--&gt;</c> is an
///     observed cross-project reference, a dotted <c>-.-&gt;</c> is a project reference that is declared and
///     never exercised. That second edge is the diagram's reason to exist — the text survey leaves the
///     reader to compute it by eye from two separate sections.
///     <para>
///         Labels carry no type or reference counts. They move on nearly every commit, and this artifact is
///         committed and drift-gated, so a count-bearing diagram would need re-rendering constantly and
///         become a permanent merge magnet. Structure-only labels change when a project or a cross-project
///         reference appears or disappears — exactly when a hand-drawn diagram would have gone stale.
///         Counts stay in the <c>graph</c> survey, which nothing commits.
///     </para>
/// </remarks>
public static class GraphDiagramRenderer
{
    // Every node ID carries this prefix, which is what keeps a project name from ever being lexed as
    // something other than a node. Mermaid's flowchart grammar (flow.jison, read 2026-07-26) has 19 lex
    // rules whose pattern is a bare word — end, graph, flowchart, subgraph, style, class, classDef,
    // linkStyle, default, interpolate, call, href, click, flowchart-elk, swimlane-beta, _self, _blank,
    // _parent, _top — and none of them is anchored to the start of a statement, so a project of any of
    // those names would break the whole diagram rather than just its own node. Matching that list instead
    // would be correct only until the grammar grows a twentieth word, and this renderer runs against other
    // people's solutions, where "graph" or "class" is a plausible project name. A prefix cannot rot.
    // It also makes the grammar's case sensitivity irrelevant rather than something to rely on, and it
    // closes a second hazard for free: the link rules read a leading o or x as an arrowhead
    // (\s*[xo<]?\-\-+[-xo>]\s*), and no prefixed ID can begin with either.
    private const string NodeIdPrefix = "p_";

    private const string EmptyScopeNodeId = NodeIdPrefix + "none";

    private const string EmptyScopeLabel = "(no projects in scope)";

    /// <summary>
    ///     The managed-block body: a provenance caption, then the fenced Mermaid diagram. The caption sits
    ///     outside the fence deliberately — nothing in the wild carries a generator banner inside diagram
    ///     source, and the fence stays paste-able into any Mermaid renderer.
    /// </summary>
    /// <param name="summary">The codebase survey to draw.</param>
    /// <param name="solutionName">The solution file name, named in the caption and the accessible title.</param>
    /// <param name="scope">The project filter; null means <see cref="DiagramScope.Everything" />.</param>
    public static string Block(GraphSummary summary, string solutionName, DiagramScope? scope = null)
    {
        Guard.NotNull(summary, nameof(summary));
        Guard.NotNullOrWhiteSpace(solutionName, nameof(solutionName));

        DiagramScope filter = scope ?? DiagramScope.Everything;
        var projects = summary.Projects.Where(project => filter.Includes(project.Name)).ToList();
        var ids = NodeIds(projects);

        var lines = new List<string>
        {
            "```mermaid",
            "flowchart LR",
            $"    accTitle: Codebase survey: {solutionName}",
            "    accDescr: Projects in this solution and their cross-project references.",
            ""
        };
        lines.AddRange(NodeLines(projects, ids));

        var edges = EdgeLines(summary, projects, ids);
        if (edges.Count > 0)
        {
            lines.Add("");
            lines.AddRange(edges);
        }

        lines.Add("```");

        return ProvenanceLine(solutionName) + "\n\n" + string.Join("\n", lines);
    }

    // The provenance/warning caption. Names the solution rather than the spec: the survey is a property of
    // the codebase, so there is no spec to go and edit — the remedy is a re-render.
    private static string ProvenanceLine(string solutionName)
    {
        return $"*Generated by `loadbearing render` from `{solutionName}` — " +
               "do not edit between the markers; re-render to update.*";
    }

    // One node per in-scope project, in the summary's ordinal order. An empty scope emits the (none)
    // placeholder rather than an empty diagram, so the artifact's shape stays stable — the same convention
    // the text survey's empty sections follow.
    private static IEnumerable<string> NodeLines(IReadOnlyList<ProjectSummary> projects, IReadOnlyDictionary<string, string> ids)
    {
        return projects.Count > 0
            ? projects.Select(project => $"    {ids[project.Name]}[\"{Label(project.Name)}\"]")
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

        var lines = summary.ProjectEdges
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
    // so two names that slug alike (MyApp.Web and MyApp-Web) still get distinct nodes.
    private static Dictionary<string, string> NodeIds(IReadOnlyList<ProjectSummary> projects)
    {
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (ProjectSummary project in projects)
        {
            string slug = NodeIdPrefix + Slug(project.Name);
            string candidate = slug;
            var suffix = 2;
            while (!taken.Add(candidate)) candidate = $"{slug}_{suffix++}";

            ids[project.Name] = candidate;
        }

        return ids;
    }

    // ASCII letters and digits survive; everything else becomes an underscore. Deliberately not
    // char.IsLetterOrDigit, which is Unicode-aware and would leave accented letters in an identifier
    // position where Mermaid's tolerance is unknown.
    private static string Slug(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (char character in name)
        {
            bool ascii = (character >= 'a' && character <= 'z')
                         || (character >= 'A' && character <= 'Z')
                         || (character >= '0' && character <= '9');
            builder.Append(ascii ? character : '_');
        }

        return builder.ToString();
    }

    // Labels are the project name verbatim, inside a quoted label, with the two characters Mermaid reads
    // as markup sent out as the entities it reads back as themselves: a double quote would end the label
    // early, and '#' opens an entity reference (#35; is the documented escape for a literal one).
    // The '#' pass must run FIRST — reversed, it would rewrite the '#' of an emitted #quot; into #35;quot;.
    private static string Label(string name)
    {
        return name
            .Replace("#", "#35;")
            .Replace("\"", "#quot;");
    }
}