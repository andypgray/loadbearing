using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     Formats a <see cref="GraphSummary" /> as the human <c>graph</c> survey — a project roster with
///     declared references and type counts, the observed cross-project reference edges, the namespace
///     inventory, and external references grouped by namespace root. Pure over the summary, so the line
///     shapes are unit-pinned. Mirrors <see cref="StatusFormatter" />'s terse, em-dashed voice; an empty
///     section reads <c>(none)</c> rather than vanishing, so the survey's shape is stable — which is also
///     why overview grain replaces the namespace inventory with an elision line instead of dropping the
///     section: a reader can see what was left out and how to get it back.
/// </summary>
internal static class GraphFormatter
{
    private const string OverviewElisionLine =
        "  (elided at overview grain — rerun without --overview for the per-project namespace inventory)";

    private const string SkeletonElisionLine =
        "  (elided at skeleton grain — rerun without --skeleton for the external references)";

    /// <summary>
    ///     The survey's lines. A coarser <paramref name="grain" /> renders the same sections with less in
    ///     them: the namespace inventory becomes one elision line at overview grain, the external references
    ///     become one more at skeleton grain, and no section ever disappears.
    /// </summary>
    public static IReadOnlyList<string> Lines(GraphSummary summary, string solutionName, GraphGrain grain)
    {
        var lines = new List<string> { $"Codebase survey: {solutionName}", "" };

        lines.Add($"Projects ({summary.Projects.Count}):");
        lines.AddRange(summary.Projects.Select(ProjectLine));
        lines.Add("");

        lines.Add("Observed project references (distinct type pairs):");
        lines.AddRange(ProjectEdgeLines(summary));
        lines.Add("");

        lines.Add("Namespaces:");
        lines.AddRange(NamespaceLines(summary, grain));
        lines.Add("");

        lines.Add("External references (by namespace root):");
        lines.AddRange(ExternalEdgeLines(summary, grain));

        return lines;
    }

    private static IEnumerable<string> NamespaceLines(GraphSummary summary, GraphGrain grain)
    {
        return grain >= GraphGrain.Overview ? [OverviewElisionLine] : summary.Projects.Select(NamespaceLine);
    }

    private static string ProjectLine(ProjectSummary project)
    {
        string references = project.ProjectReferences.Count > 0 ? string.Join(", ", project.ProjectReferences) : "(none)";
        return $"  {project.Name} — {project.Types} {Plural(project.Types, "type")}; references: {references}";
    }

    private static string NamespaceLine(ProjectSummary project)
    {
        string inventory = project.Namespaces.Count > 0
            ? string.Join(", ", project.Namespaces.Select(n => $"{n.Namespace} ({n.Types})"))
            : "(none)";
        return $"  {project.Name}: {inventory}";
    }

    private static IEnumerable<string> ProjectEdgeLines(GraphSummary summary)
    {
        return summary.ProjectEdges.Count > 0
            ? summary.ProjectEdges.Select(e => $"  {e.Source} -> {e.Target}: {e.References}")
            : ["  (none)"];
    }

    private static IEnumerable<string> ExternalEdgeLines(GraphSummary summary, GraphGrain grain)
    {
        if (grain >= GraphGrain.Skeleton) return [SkeletonElisionLine];

        return summary.ExternalEdges.Count > 0
            ? summary.ExternalEdges.Select(e => $"  {e.Source} -> {e.TargetNamespaceRoot}: {e.References}")
            : ["  (none)"];
    }

    private static string Plural(int count, string noun)
    {
        return count == 1 ? noun : noun + "s";
    }
}
