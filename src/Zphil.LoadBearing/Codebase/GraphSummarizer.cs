using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     Turns a <see cref="CodebaseModel" /> into a <see cref="GraphSummary" />: the survey of a codebase
///     that needs no architecture spec, which is what the CLI's <c>graph</c> verb prints. Pure counting
///     over a model that is already ordered, with no I/O and no compiler work, and every list it produces
///     is ordinal-ordered, so one model always summarizes to the same document.
/// </summary>
public static class GraphSummarizer
{
    private const string GlobalNamespaceLabel = "(global)";

    /// <summary>
    ///     Builds the survey from an extracted model: every project with its namespace inventory, the
    ///     reference edges observed between projects, the external references grouped by namespace root, and
    ///     the names more than one place declares. Narrow the result afterwards with <see cref="Scope" />.
    /// </summary>
    /// <remarks>
    ///     The project edges are the references the code actually makes, each read at the project that made
    ///     it. Where one source file is compiled into several projects that distinction bites: a reference
    ///     into a type the referencing project compiles itself is not an edge between projects, and a
    ///     reference out of such a type is one edge from every project that compiled it.
    ///     <see cref="GraphSummary.MultiplyDeclaredTypes" /> states the attribution all of that rests on
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
        List<ProjectEdgeSummary> projectEdges = CrossProjectPairs(model)
            .GroupBy(pair => pair)
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

        // The coverage statement behind the instance reading above, and the fact a rule author needs
        // before anchoring a subject on a project. Types are already ordinal by full name; the sort is
        // spelled anyway so this list's stated order does not depend on the model's.
        List<MultiplyDeclaredTypeSummary> multiplyDeclaredTypes = model.Types
            .Where(type => type.AlsoDeclaredBy.Count > 0)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(type => new MultiplyDeclaredTypeSummary(type.FullName, DeclarersOf(type), type.ProjectName))
            .ToList();

        return new GraphSummary(projects, projectEdges, externalEdges, multiplyDeclaredTypes, ShadowedTypes(model));
    }

    // The second coverage statement, and the only place a name in this model does not identify a type: a
    // project declares a full name that a referenced assembly also supplies, so Types carries both nodes.
    // A projection of the fact the merge stamped, never re-derived by grouping the type universe — reading
    // the stamped fact is also the only way to state the binder roster truthfully, because the edges see
    // one name per edge minted, which is not every name a project binds.
    private static List<ShadowedTypeSummary> ShadowedTypes(CodebaseModel model)
    {
        return model.ShadowedNames
            .Select(shadowed => new ShadowedTypeSummary(
                shadowed.FullName, shadowed.DeclaredBy, shadowed.SuppliedBy, shadowed.BoundFromAssemblyBy))
            .ToList();
    }

    // The project pairs the code actually declares, one per edge instance that crosses a boundary. The
    // model carries one node per full name and one edge per type PAIR, so a file compiled into several
    // projects collapses N compilations of one reference into one entry; EdgeInstances is what unfolds it,
    // and reading the same enumeration the checker's verdict reads is what keeps the survey from leading a
    // rule author into a red the spec surface cannot fix. Single attribution gets it wrong in both
    // directions: a project compiling its own linked-in copy would reach a node stamped with somebody
    // else's name, inventing a dependency no project file declares, and a MULTIPLY-declared source read at
    // its winner alone would lose every other declarer's genuine outward edge. An instance is emitted per
    // (declarer, reached) pair, so a pair is still counted once per type pair per project pair.
    private static IEnumerable<(string Source, string Target)> CrossProjectPairs(CodebaseModel model)
    {
        foreach (ReferenceEdge edge in model.Edges)
        {
            if (edge.Target.IsExternal) continue;

            foreach (EdgeInstance instance in EdgeInstances.Of(edge.Source, edge.Target))
                if (!instance.IsIntraProject)
                    yield return (instance.SourceProject, instance.TargetProject);
        }
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
    ///     complete survey of a smaller subject, not a truncated one. A pattern is matched against the whole
    ///     project (assembly) name, case-sensitively, with <c>*</c> standing for any run of characters
    ///     including none, so <c>MyApp.*</c> keeps every project whose name begins <c>MyApp.</c>. An empty
    ///     list narrows nothing and hands <paramref name="summary" /> straight back.
    /// </summary>
    /// <remarks>
    ///     What survives is wider than the project list, so a narrowed survey can name projects it does not
    ///     list. A project edge survives when either end matches, which keeps visible who reaches into the
    ///     matched projects, so <see cref="GraphSummary.ProjectEdges" /> may name a project absent from
    ///     <see cref="GraphSummary.Projects" />. An external edge belongs to one project, so a match on its
    ///     source keeps it. Each surviving <see cref="ProjectSummary" /> is carried through unchanged, its
    ///     declared <see cref="ProjectSummary.ProjectReferences" /> included, so a declared reference to a
    ///     project outside the narrowing stays visible. A <see cref="GraphSummary.MultiplyDeclaredTypes" />
    ///     entry survives when any of its declaring projects matches, and a
    ///     <see cref="GraphSummary.ShadowedTypes" /> entry when either its
    ///     <see cref="ShadowedTypeSummary.DeclaredBy" /> project or one of its
    ///     <see cref="ShadowedTypeSummary.BoundFromAssemblyBy" /> projects does; those two are rarely the same
    ///     project, so matching on the declarer alone would drop the entry from the very survey that needs it.
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

        List<string> packageReferences = project.PackageReferences
            .Select(package => package.Name)
            .ToList();

        return new ProjectSummary(
            project.Name, project.ProjectReferences, declaredTypes.Count, generated, namespaces,
            project.SolutionMember, project.TargetFrameworks, project.FactsFollow, packageReferences,
            project.IsPackable, project.LocksPackages);
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
