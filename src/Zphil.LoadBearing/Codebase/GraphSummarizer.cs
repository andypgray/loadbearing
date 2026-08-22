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
    /// <remarks>
    ///     The project edges are the edges the code actually declares: a reference into a type the
    ///     referencing project compiles itself is not one, however extraction attributed that type, and
    ///     <see cref="GraphSummary.MultiplyDeclaredTypes" /> states the attribution that suppression rests on
    ///     rather than leaving it silent.
    /// </remarks>
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
            .Where(edge => !edge.Target.IsExternal
                           && edge.Source.ProjectName != edge.Target.ProjectName
                           && !SourceAlsoDeclaresTarget(edge))
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

        // The coverage statement behind the suppression above, and the fact a rule author needs before
        // anchoring a subject on a project. Types are already ordinal by full name; the sort is spelled
        // anyway so this list's stated order does not depend on the model's.
        List<MultiplyDeclaredTypeSummary> multiplyDeclaredTypes = model.Types
            .Where(type => type.AlsoDeclaredBy.Count > 0)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(type => new MultiplyDeclaredTypeSummary(type.FullName, DeclarersOf(type), type.ProjectName))
            .ToList();

        return new GraphSummary(projects, projectEdges, externalEdges, multiplyDeclaredTypes, ShadowedTypes(model));
    }

    // The second coverage statement, and the only place a name in this model does not identify a type: a
    // project declares a full name that a referenced assembly also supplies, so Types carries both nodes.
    // A projection of the fact the merge stamped, the shape MultiplyDeclaredTypes above already takes — so
    // the survey's two coverage statements read the same way, and neither rediscovers its subject by
    // grouping the type universe on name. Reading the stamped fact is also the only way to state the binder
    // roster truthfully: the edges see one name per edge minted, which is not every name a project binds.
    private static List<ShadowedTypeSummary> ShadowedTypes(CodebaseModel model)
    {
        return model.ShadowedNames
            .Select(shadowed => new ShadowedTypeSummary(
                shadowed.FullName, shadowed.DeclaredBy, shadowed.SuppliedBy, shadowed.BoundFromAssemblyBy))
            .ToList();
    }

    // A reference from a project into a type that project declares itself. Extraction attributes a
    // multiply-declared type to its first declarer alone, so a project compiling its own linked-in copy
    // reaches a node stamped with somebody else's name — and rendering that as a cross-project edge invents
    // a dependency no project file declares. Because model.Edges is one entry per type PAIR, dropping it
    // here removes exactly those pairs and leaves every genuine pair between the same two projects, and its
    // count, untouched.
    private static bool SourceAlsoDeclaresTarget(ReferenceEdge edge)
    {
        return edge.Target.AlsoDeclaredBy.Contains(edge.Source.ProjectName, StringComparer.Ordinal);
    }

    // Every declarer of a conflated type: the winner and the losers as one ordinal roster, which is what
    // makes an entry readable on its own — "these projects declare it, that one's facts won" — rather than
    // a losers list a reader has to add the winner back into.
    private static IReadOnlyList<string> DeclarersOf(TypeNode type)
    {
        var declarers = new List<string>(type.AlsoDeclaredBy) { type.ProjectName };
        declarers.Sort(StringComparer.Ordinal);
        return declarers;
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
    ///     <para>
    ///         A <see cref="GraphSummary.MultiplyDeclaredTypes" /> entry survives when <em>any</em> of its
    ///         declaring projects is in scope, the same either-endpoint rule the project edges take: the
    ///         reason to read the entry is that a subject anchored inside the scope will miss the type, and
    ///         a declarer outside it is the half that explains why.
    ///     </para>
    ///     <para>
    ///         A <see cref="GraphSummary.ShadowedTypes" /> entry takes the same either-end rule across its two
    ///         different ends: it survives when its <see cref="ShadowedTypeSummary.DeclaredBy" /> project is in
    ///         scope, or when any of its <see cref="ShadowedTypeSummary.BoundFromAssemblyBy" /> projects is.
    ///         Those are rarely the same project — the declarer is the test assembly carrying the stand-in,
    ///         the binder the product code reaching the package — so narrowing on the declarer alone would
    ///         drop the entry from exactly the scope whose author needs it.
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

        List<MultiplyDeclaredTypeSummary> multiplyDeclaredTypes = summary.MultiplyDeclaredTypes
            .Where(type => type.DeclaredBy.Any(project => Matches(projectGlobs, project)))
            .ToList();

        // Either end keeps the entry, the rule the project edges take: the reader anchored on the product
        // project is the one whose reference reaches the assembly rather than the declaration, and narrowing
        // on the declaring project alone would drop the entry from exactly the scope that needs it.
        List<ShadowedTypeSummary> shadowedTypes = summary.ShadowedTypes
            .Where(type => Matches(projectGlobs, type.DeclaredBy)
                           || type.BoundFromAssemblyBy.Any(project => Matches(projectGlobs, project)))
            .ToList();

        return new GraphSummary(projects, projectEdges, externalEdges, multiplyDeclaredTypes, shadowedTypes);
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
            .Select(group => new NamespaceCount(
                DisplayNamespace(group.Key), group.Count(), group.Count(type => type.IsGenerated)))
            .ToList();

        int generated = declaredTypes.Count(type => type.IsGenerated);

        return new ProjectSummary(
            project.Name, project.ProjectReferences, declaredTypes.Count, generated, namespaces,
            project.SolutionMember, project.TargetFrameworks, project.FactsFollow);
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
