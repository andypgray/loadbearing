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

    private const string SkeletonShadowedElisionLine =
        "  (elided at skeleton grain — rerun without --skeleton for the shadowed type names)";

    /// <summary>
    ///     The survey's lines. A coarser <paramref name="grain" /> renders the same sections with less in
    ///     them: the namespace inventory becomes one elision line at overview grain, the external references
    ///     and the multiply-declared types become elision lines at skeleton grain, and no section ever
    ///     disappears. A section with nothing to elide keeps its <c>(none)</c> instead, which is why a
    ///     healthy solution's skeleton still says outright that no type is declared twice.
    /// </summary>
    /// <param name="summary">The survey to format.</param>
    /// <param name="solutionName">The solution's file name, for the heading.</param>
    /// <param name="grain">How much of each section to render.</param>
    /// <param name="unsupportedProjects">
    ///     The declared projects this product could not read, as the shared trust stamp composed them —
    ///     relativized, and each already carrying its reason, so this line and the document's own key cannot
    ///     disagree about what the run could read. Passed in rather than taken off <paramref name="summary" />
    ///     because it is a fact about the load rather than about the codebase: the summary describes what was
    ///     extracted, and this names what never could be. For that reason it is also unaffected by
    ///     <c>--projects</c>, which scopes the summary alone.
    /// </param>
    public static IReadOnlyList<string> Lines(
        GraphSummary summary, string solutionName, DocumentGrain grain,
        IReadOnlyList<UnsupportedProjectStamp> unsupportedProjects)
    {
        var lines = new List<string> { $"Codebase survey: {solutionName}", "" };

        lines.Add($"Projects ({summary.Projects.Count}):");
        lines.AddRange(summary.Projects.Select(ProjectLine));
        lines.Add("");

        lines.Add("Projects the solution declares that this survey could not read:");
        lines.AddRange(UnsupportedProjectLines(unsupportedProjects));
        lines.Add("");

        lines.Add("Observed project references (distinct type pairs):");
        lines.AddRange(ProjectEdgeLines(summary));
        lines.Add("");

        lines.Add("Types declared by more than one project:");
        lines.AddRange(MultiplyDeclaredTypeLines(summary, grain));
        lines.Add("");

        lines.Add("Type names a referenced assembly also supplies:");
        lines.AddRange(ShadowedTypeLines(summary, grain));
        lines.Add("");

        lines.Add("Namespaces:");
        lines.AddRange(NamespaceLines(summary, grain));
        lines.Add("");

        lines.Add("External references (by namespace root):");
        lines.AddRange(ExternalEdgeLines(summary, grain));

        return lines;
    }

    // Sited straight under the roster, because it is the roster's own caveat: these are the projects that
    // would have been lines above it. Reads "(none)" on an all-C# solution rather than vanishing, per this
    // formatter's standing rule — a section that appears only when it has content is one a reader never
    // learns to look for, and this is the section whose absence was the defect. Never elided at any grain:
    // it is bounded by the solution rather than the codebase, and a coarser survey is where a reader most
    // needs to know it is of part of the solution.
    private static IEnumerable<string> UnsupportedProjectLines(
        IReadOnlyList<UnsupportedProjectStamp> unsupportedProjects)
    {
        return unsupportedProjects.Count > 0
            ? unsupportedProjects.Select(project => $"  {project.Describe()}")
            : ["  (none)"];
    }

    private static IEnumerable<string> NamespaceLines(GraphSummary summary, DocumentGrain grain)
    {
        return grain >= DocumentGrain.Overview ? [OverviewElisionLine] : summary.Projects.Select(NamespaceLine);
    }

    // Only the passenger is annotated. Membership is the unremarkable case — every project of a healthy
    // solution has it — so marking it would put a badge on every line and leave the one line worth reading
    // no easier to find. An unread membership says nothing at all, for the same reason it serializes absent.
    // The framework clause follows that same rule: one project file, one compilation is the unremarkable
    // case, so only the project that arrived as several says so.
    private static string ProjectLine(ProjectSummary project)
    {
        string references = project.ProjectReferences.Count > 0 ? string.Join(", ", project.ProjectReferences) : "(none)";
        string membership = project.SolutionMember == false ? " (not a solution member)" : "";
        string generated = project.Generated > 0 ? $" ({project.Generated} generated)" : "";
        return $"  {project.Name}{membership} — {project.Types} {Plurals.Noun(project.Types, "type")}{generated}; "
               + $"{FrameworksClause(project)}references: {references}";
    }

    // "targets net10.0, netstandard2.0 (shared types from net10.0); " — the frameworks in extraction order,
    // and the winner only where the frameworks actually share a type. A multi-targeted project whose
    // frameworks share nothing displaced no facts, so the parenthesis would be a claim about nothing; the
    // list alone still says the project compiles more than once, which is the fact a rule author needs.
    private static string FrameworksClause(ProjectSummary project)
    {
        if (project.TargetFrameworks.Count == 0) return "";

        string shared = project.FactsFollow is { } winner ? $" (shared types from {winner})" : "";
        return $"targets {string.Join(", ", project.TargetFrameworks)}{shared}; ";
    }

    private static string NamespaceLine(ProjectSummary project)
    {
        string inventory = project.Namespaces.Count > 0
            ? string.Join(", ", project.Namespaces.Select(NamespaceEntry))
            : "(none)";
        return $"  {project.Name}: {inventory}";
    }

    // A wholly generated namespace reads "all generated" rather than repeating the count it just gave. That
    // is the shape a reader most needs to catch — a compiled view tier collects under one namespace nobody
    // typed, and it is the one namespace here that must never become a layer glob.
    private static string NamespaceEntry(NamespaceCount @namespace)
    {
        if (@namespace.Generated == 0) return $"{@namespace.Namespace} ({@namespace.Types})";

        string qualifier = @namespace.Generated == @namespace.Types ? "all generated" : $"{@namespace.Generated} generated";
        return $"{@namespace.Namespace} ({@namespace.Types}, {qualifier})";
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
        return CoverageSection(
            summary.MultiplyDeclaredTypes, grain, SkeletonMultiplyDeclaredElisionLine, MultiplyDeclaredTypeLine);
    }

    private static string MultiplyDeclaredTypeLine(MultiplyDeclaredTypeSummary type)
    {
        return $"  {type.Type} — declared by {string.Join(", ", type.DeclaredBy)}; "
               + $"facts follow {type.FactsFollow}";
    }

    // The second coverage statement, sited beside the first because they answer the same question — why a
    // selection over a project does not reach every use of a name it declares. This one's answer is that the
    // name means two types, and the line names both halves plus who reaches the far one.
    private static IEnumerable<string> ShadowedTypeLines(GraphSummary summary, DocumentGrain grain)
    {
        return CoverageSection(summary.ShadowedTypes, grain, SkeletonShadowedElisionLine, ShadowedTypeLine);
    }

    // No empty-roster arm: an entry exists only because some project's compilation bound the name from the
    // assembly, and the merge records that project where it knows it rather than inferring it from the edges,
    // so "which nothing binds" is a sentence this line can no longer be asked to write.
    private static string ShadowedTypeLine(ShadowedTypeSummary type)
    {
        return $"  {type.Type} — declared by {type.DeclaredBy}; also supplied by {string.Join(", ", type.SuppliedBy)}, "
               + $"which {string.Join(", ", type.BoundFromAssemblyBy)} binds";
    }

    // The rule both coverage statements above take, and the one that sets them apart from the sections
    // around them: an empty statement still reads "(none)" at every grain, because having nothing to report
    // IS its finding, and only a non-empty one elides its rows at skeleton, where the list scales with the
    // codebase and the fact that there is something there to read is what a coarse survey has room for.
    private static IEnumerable<string> CoverageSection<T>(
        IReadOnlyList<T> items, DocumentGrain grain, string elisionLine, Func<T, string> line)
    {
        if (items.Count == 0) return ["  (none)"];

        return grain >= DocumentGrain.Skeleton ? [elisionLine] : items.Select(line);
    }

    private static IEnumerable<string> ExternalEdgeLines(GraphSummary summary, DocumentGrain grain)
    {
        if (grain >= DocumentGrain.Skeleton) return [SkeletonElisionLine];

        return summary.ExternalEdges.Count > 0
            ? summary.ExternalEdges.Select(e => $"  {e.Source} -> {e.TargetNamespaceRoot}: {e.References}")
            : ["  (none)"];
    }
}
