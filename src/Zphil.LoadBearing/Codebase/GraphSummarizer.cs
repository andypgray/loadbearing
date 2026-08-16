using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     Summarizes a <see cref="CodebaseModel" /> into a <see cref="GraphSummary" /> — the pre-spec survey
///     the derive flow orients on. Pure over an already-deterministic model (no I/O, no Roslyn), and
///     every result list is ordinal-ordered, so the survey is byte-stable across runs.
/// </summary>
public static class GraphSummarizer
{
    private const string GlobalNamespaceLabel = "(global)";

    /// <summary>Builds the survey from an extracted model.</summary>
    /// <param name="model">The extracted codebase to summarize.</param>
    /// <returns>The survey over every project in <paramref name="model" />.</returns>
    public static GraphSummary Summarize(CodebaseModel model)
    {
        // One pass over the type universe rather than one per project: a per-project scan makes the survey
        // O(projects x types), and orienting on a large unfamiliar solution is the whole job. The lookup
        // preserves Types order within each group, and a project that declares nothing gets an empty group.
        ILookup<string, TypeNode> declaredByProject = model.Types
            .Where(type => !type.IsExternal)
            .ToLookup(type => type.ProjectName, StringComparer.Ordinal);

        List<ProjectSummary> projects = model.Projects
            .Select(project => SummarizeProject(project, declaredByProject[project.Name]))
            .ToList();

        // Cross-project edges only: a same-project reference is never a cross-boundary rule candidate, so
        // it is excluded from the survey (the survey exists to seed layering/boundary rules).
        List<ProjectEdgeSummary> projectEdges = model.Edges
            .Where(edge => !edge.Target.IsExternal && edge.Source.ProjectName != edge.Target.ProjectName)
            .GroupBy(edge => (Source: edge.Source.ProjectName, Target: edge.Target.ProjectName))
            .Select(group => new ProjectEdgeSummary(group.Key.Source, group.Key.Target, group.Count()))
            .OrderBy(edge => edge.Source, StringComparer.Ordinal)
            .ThenBy(edge => edge.Target, StringComparer.Ordinal)
            .ToList();

        List<ExternalEdgeSummary> externalEdges = model.Edges
            .Where(edge => edge.Target.IsExternal)
            .GroupBy(edge => (Source: edge.Source.ProjectName, Root: NamespaceRoot(edge.Target.Namespace)))
            .Select(group => new ExternalEdgeSummary(group.Key.Source, group.Key.Root, group.Count()))
            .OrderBy(edge => edge.Source, StringComparer.Ordinal)
            .ThenBy(edge => edge.TargetNamespaceRoot, StringComparer.Ordinal)
            .ToList();

        return new GraphSummary(projects, projectEdges, externalEdges);
    }

    /// <summary>
    ///     Narrows a survey to the projects whose name matches one of <paramref name="projectGlobs" /> — a
    ///     complete survey of a smaller subject, not a truncated one. A pattern matches the project
    ///     (assembly) name as a single ordinal token through the shared glob matcher, where <c>*</c> spans
    ///     any run of characters; an empty list narrows nothing and hands the summary straight back.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         An observed project edge survives when <em>either</em> end is in scope. Who reaches into the
    ///         scoped projects is the evidence a layering rule is drafted from, so dropping inbound edges
    ///         would hide the half of the graph the reader came for. The deliberate consequence: a scoped
    ///         survey's <see cref="GraphSummary.ProjectEdges" /> can name projects absent from
    ///         <see cref="GraphSummary.Projects" />. External edges are attributed to one project, so they
    ///         survive on a source match alone.
    ///     </para>
    ///     <para>
    ///         Each surviving <see cref="ProjectSummary" /> is carried through verbatim, declared
    ///         <see cref="ProjectSummary.ProjectReferences" /> included: a declared reference to a project
    ///         outside the scope is exactly the divergence signal, and filtering it would erase it.
    ///     </para>
    /// </remarks>
    /// <param name="summary">The survey to narrow.</param>
    /// <param name="projectGlobs">The project-name globs; empty means every project.</param>
    /// <returns>A survey of the matching projects, or <paramref name="summary" /> itself when nothing narrows.</returns>
    public static GraphSummary Scope(GraphSummary summary, IReadOnlyList<string> projectGlobs)
    {
        Guard.NotNull(summary, nameof(summary));
        Guard.NotNull(projectGlobs, nameof(projectGlobs));

        if (projectGlobs.Count == 0) return summary;

        List<ProjectSummary> projects = summary.Projects
            .Where(project => Matches(projectGlobs, project.Name))
            .ToList();

        List<ProjectEdgeSummary> projectEdges = summary.ProjectEdges
            .Where(edge => Matches(projectGlobs, edge.Source) || Matches(projectGlobs, edge.Target))
            .ToList();

        List<ExternalEdgeSummary> externalEdges = summary.ExternalEdges
            .Where(edge => Matches(projectGlobs, edge.Source))
            .ToList();

        return new GraphSummary(projects, projectEdges, externalEdges);
    }

    private static bool Matches(IReadOnlyList<string> globs, string projectName)
    {
        return globs.Any(glob => Wildcard.Match(glob, projectName));
    }

    private static ProjectSummary SummarizeProject(ProjectNode project, IEnumerable<TypeNode> declared)
    {
        List<TypeNode> declaredTypes = declared.ToList();

        List<NamespaceCount> namespaces = declaredTypes
            .GroupBy(type => type.Namespace)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new NamespaceCount(DisplayNamespace(group.Key), group.Count()))
            .ToList();

        return new ProjectSummary(
            project.Name, project.ProjectReferences, declaredTypes.Count, namespaces, project.SolutionMember);
    }

    // The external-reference bucket: the first two dot-segments of the target's namespace (one segment →
    // that segment; the global namespace → the (global) label), so a wide BCL surface collapses to a
    // small, reviewable shortlist of roots (System.Data, System.Text, …) rather than a per-type dump.
    private static string NamespaceRoot(string @namespace)
    {
        if (@namespace.Length == 0) return GlobalNamespaceLabel;

        // Found by scanning to the second dot rather than splitting: this runs once per external reference
        // edge — the largest edge population in the model — and a split allocates an array plus a substring
        // per segment to keep two of them.
        int firstDot = @namespace.IndexOf('.');
        if (firstDot < 0) return @namespace;

        int secondDot = @namespace.IndexOf('.', firstDot + 1);
        return secondDot < 0 ? @namespace : @namespace.Substring(0, secondDot);
    }

    private static string DisplayNamespace(string @namespace)
    {
        return @namespace.Length == 0 ? GlobalNamespaceLabel : @namespace;
    }
}
