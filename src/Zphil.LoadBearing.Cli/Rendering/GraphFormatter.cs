using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Rendering;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     Formats a <see cref="GraphSummary" /> as the human <c>graph</c> survey — a project roster with
///     declared references and type counts, the observed cross-project reference edges, the types more than
///     one project declares, the namespace inventory, and external references grouped by namespace root.
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

    private const string SkeletonMultiplyDeclaredElisionLine =
        "  (elided at skeleton grain — rerun without --skeleton for the multiply-declared types)";

    /// <summary>
    ///     The survey's lines. A coarser <paramref name="grain" /> renders the same sections with less in
    ///     them: the namespace inventory becomes one elision line at overview grain, the external references
    ///     and the multiply-declared types become elision lines at skeleton grain, and no section ever
    ///     disappears. A section with nothing to elide keeps its <c>(none)</c> instead, which is why a
    ///     healthy solution's skeleton still says outright that no type is declared twice.
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

        lines.Add("Types declared by more than one project:");
        lines.AddRange(MultiplyDeclaredTypeLines(summary, grain));
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

    // The survey's coverage statement, sited straight after the edges it explains: one reason a project
    // pair is absent from the block above is that one of them compiles the other's type itself. It reads
    // "(none)" on a healthy solution rather than vanishing, for the same reason every other section does —
    // a section that only appears when it has content is one a reader never learns to look for.
    private static IEnumerable<string> MultiplyDeclaredTypeLines(GraphSummary summary, DocumentGrain grain)
    {
        if (grain >= DocumentGrain.Skeleton && summary.MultiplyDeclaredTypes.Count > 0)
            return [SkeletonMultiplyDeclaredElisionLine];

        return summary.MultiplyDeclaredTypes.Count > 0
            ? summary.MultiplyDeclaredTypes.Select(MultiplyDeclaredTypeLine)
            : ["  (none)"];
    }

    private static string MultiplyDeclaredTypeLine(MultiplyDeclaredTypeSummary type)
    {
        return $"  {type.Type} — declared by {string.Join(", ", type.DeclaredBy)}; "
               + $"facts follow {type.FactsFollow}";
    }

    private static IEnumerable<string> ExternalEdgeLines(GraphSummary summary, DocumentGrain grain)
    {
        if (grain >= DocumentGrain.Skeleton) return [SkeletonElisionLine];

        return summary.ExternalEdges.Count > 0
            ? summary.ExternalEdges.Select(e => $"  {e.Source} -> {e.TargetNamespaceRoot}: {e.References}")
            : ["  (none)"];
    }
}
