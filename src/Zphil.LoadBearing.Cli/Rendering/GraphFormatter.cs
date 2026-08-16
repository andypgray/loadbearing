using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     Formats a <see cref="GraphSummary" /> as the human <c>graph</c> survey — a project roster with
///     declared references and type counts, the observed cross-project reference edges, the namespace
///     inventory, and external references grouped by namespace root.
/// </summary>
/// <remarks>
///     Pure over the summary, so the line shapes are unit-pinned. Mirrors
///     <see cref="StatusFormatter" />'s terse, em-dashed voice; an empty section reads <c>(none)</c> rather
///     than vanishing, so the survey's shape is stable — which is also why overview grain replaces the
///     namespace inventory with an elision line instead of dropping the section: a reader can see what was
///     left out and how to get it back.
/// </remarks>
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
    public static IReadOnlyList<string> Lines(GraphSummary summary, string solutionName, DocumentGrain grain)
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

    private static IEnumerable<string> NamespaceLines(GraphSummary summary, DocumentGrain grain)
    {
        return grain >= DocumentGrain.Overview ? [OverviewElisionLine] : summary.Projects.Select(NamespaceLine);
    }

    // Only the passenger is annotated. Membership is the unremarkable case — every project of a healthy
    // solution has it — so marking it would put a badge on every line and leave the one line worth reading
    // no easier to find. An unread membership says nothing at all, for the same reason it serializes absent.
    private static string ProjectLine(ProjectSummary project)
    {
        string references = project.ProjectReferences.Count > 0 ? string.Join(", ", project.ProjectReferences) : "(none)";
        string membership = project.SolutionMember == false ? " (not a solution member)" : "";
        return $"  {project.Name}{membership} — {project.Types} {Plurals.Noun(project.Types, "type")}; "
               + $"references: {references}";
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

    private static IEnumerable<string> ExternalEdgeLines(GraphSummary summary, DocumentGrain grain)
    {
        if (grain >= DocumentGrain.Skeleton) return [SkeletonElisionLine];

        return summary.ExternalEdges.Count > 0
            ? summary.ExternalEdges.Select(e => $"  {e.Source} -> {e.TargetNamespaceRoot}: {e.References}")
            : ["  (none)"];
    }
}
