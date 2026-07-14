using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     Formats a <see cref="GraphSummary" /> as the human <c>graph</c> survey — a project roster with
///     declared references and type counts, the observed cross-project reference edges, the namespace
///     inventory, and external references grouped by namespace root. Pure over the summary, so the line
///     shapes are unit-pinned. Mirrors <see cref="StatusFormatter" />'s terse, em-dashed voice; an empty
///     section reads <c>(none)</c> rather than vanishing, so the survey's shape is stable.
/// </summary>
internal static class GraphFormatter
{
    public static IReadOnlyList<string> Lines(GraphSummary summary, string solutionName)
    {
        var lines = new List<string> { $"Codebase survey: {solutionName}", "" };

        lines.Add($"Projects ({summary.Projects.Count}):");
        lines.AddRange(summary.Projects.Select(ProjectLine));
        lines.Add("");

        lines.Add("Observed project references (distinct type pairs):");
        lines.AddRange(ProjectEdgeLines(summary));
        lines.Add("");

        lines.Add("Namespaces:");
        lines.AddRange(summary.Projects.Select(NamespaceLine));
        lines.Add("");

        lines.Add("External references (by namespace root):");
        lines.AddRange(ExternalEdgeLines(summary));

        return lines;
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

    private static IEnumerable<string> ExternalEdgeLines(GraphSummary summary)
    {
        return summary.ExternalEdges.Count > 0
            ? summary.ExternalEdges.Select(e => $"  {e.Source} -> {e.TargetNamespaceRoot}: {e.References}")
            : ["  (none)"];
    }

    private static string Plural(int count, string noun)
    {
        return count == 1 ? noun : noun + "s";
    }
}