namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     Summarizes a <see cref="CodebaseModel" /> into a <see cref="GraphSummary" /> — the pre-spec survey
///     the Phase 8 derive flow orients on. Pure over an already-deterministic model (no I/O, no Roslyn):
///     projects with namespace inventories, observed cross-project reference edges grouped by project
///     pair, and external references grouped by namespace root. Every result list is ordinal-ordered, so
///     both render targets (the human survey and <c>graph --json</c>) are byte-stable.
/// </summary>
public static class GraphSummarizer
{
    private const string GlobalNamespaceLabel = "(global)";

    /// <summary>Builds the survey from an extracted model.</summary>
    public static GraphSummary Summarize(CodebaseModel model)
    {
        var projects = model.Projects
            .Select(project => SummarizeProject(project, model))
            .ToList();

        // Cross-project edges only: a same-project reference is never a cross-boundary rule candidate, so
        // it is excluded from the survey (the survey exists to seed layering/boundary rules).
        var projectEdges = model.Edges
            .Where(edge => !edge.Target.IsExternal && edge.Source.ProjectName != edge.Target.ProjectName)
            .GroupBy(edge => (Source: edge.Source.ProjectName, Target: edge.Target.ProjectName))
            .Select(group => new ProjectEdgeSummary(group.Key.Source, group.Key.Target, group.Count()))
            .OrderBy(edge => edge.Source, StringComparer.Ordinal)
            .ThenBy(edge => edge.Target, StringComparer.Ordinal)
            .ToList();

        var externalEdges = model.Edges
            .Where(edge => edge.Target.IsExternal)
            .GroupBy(edge => (Source: edge.Source.ProjectName, Root: NamespaceRoot(edge.Target.Namespace)))
            .Select(group => new ExternalEdgeSummary(group.Key.Source, group.Key.Root, group.Count()))
            .OrderBy(edge => edge.Source, StringComparer.Ordinal)
            .ThenBy(edge => edge.TargetNamespaceRoot, StringComparer.Ordinal)
            .ToList();

        return new GraphSummary(projects, projectEdges, externalEdges);
    }

    private static ProjectSummary SummarizeProject(ProjectNode project, CodebaseModel model)
    {
        var declaredTypes = model.Types
            .Where(type => !type.IsExternal && type.ProjectName == project.Name)
            .ToList();

        var namespaces = declaredTypes
            .GroupBy(type => type.Namespace)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new NamespaceCount(DisplayNamespace(group.Key), group.Count()))
            .ToList();

        return new ProjectSummary(project.Name, project.ProjectReferences, declaredTypes.Count, namespaces);
    }

    // The external-reference bucket: the first two dot-segments of the target's namespace (one segment →
    // that segment; the global namespace → the (global) label), so a wide BCL surface collapses to a
    // small, reviewable shortlist of roots (System.Data, System.Text, …) rather than a per-type dump.
    private static string NamespaceRoot(string @namespace)
    {
        if (@namespace.Length == 0) return GlobalNamespaceLabel;

        string[] segments = @namespace.Split('.');
        return segments.Length >= 2 ? $"{segments[0]}.{segments[1]}" : segments[0];
    }

    private static string DisplayNamespace(string @namespace)
    {
        return @namespace.Length == 0 ? GlobalNamespaceLabel : @namespace;
    }
}