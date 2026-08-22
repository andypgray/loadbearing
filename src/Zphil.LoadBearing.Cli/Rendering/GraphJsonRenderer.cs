using System.Text.Json;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn.Diagnostics;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     Renders a <see cref="GraphSummary" /> as the <c>graph --json</c> document (its own schemaVersion 1)
///     — the pre-spec codebase survey. Uses the shared <see cref="LoadBearingJson.Options" />;
///     machine-independent (<c>solution</c> is a file name). Grouped counts only, no per-site dumps.
/// </summary>
/// <remarks>
///     The document is composed as a string rather than written straight out, so a caller with a response
///     budget can measure the full survey and, if it overruns, re-compose it at overview grain from the same
///     summary — one extraction, two renders, and never a document cut mid-array.
/// </remarks>
internal static class GraphJsonRenderer
{
    /// <summary>
    ///     The survey document as a string. <paramref name="grain" /> decides how much of each project is
    ///     rendered and stamps itself on the document; <paramref name="projectsScope" /> is the filter the
    ///     summary was already narrowed by, recorded so the document says what it covers.
    ///     <paramref name="workspaceDiagnostics" /> is the rendered stream the caller composed and
    ///     <paramref name="diagnostics" /> the load's own verdict, whose project lists become the trust stamps.
    /// </summary>
    public static string Document(
        GraphSummary summary,
        string solutionDirectory,
        string solutionName,
        IReadOnlyList<string> workspaceDiagnostics,
        WorkspaceDiagnostics diagnostics,
        DocumentGrain grain,
        IReadOnlyList<string> projectsScope)
    {
        bool skeleton = grain >= DocumentGrain.Skeleton;

        (IReadOnlyList<GraphMultiplyDeclaredTypeJson>? multiplyDeclaredRows, int? multiplyDeclaredCount) =
            CoverageRung(
                summary.MultiplyDeclaredTypes, skeleton,
                t => new GraphMultiplyDeclaredTypeJson(t.Type, t.DeclaredBy, t.FactsFollow));
        (IReadOnlyList<GraphShadowedTypeJson>? shadowedRows, int? shadowedCount) =
            CoverageRung(
                summary.ShadowedTypes, skeleton,
                t => new GraphShadowedTypeJson(t.Type, t.DeclaredBy, t.SuppliedBy, t.BoundFromAssemblyBy));
        WorkspaceTrustStamp trust = WorkspaceTrustStamp.From(diagnostics, solutionDirectory);

        var document = new GraphJson(
            1,
            solutionName,
            DocumentGrains.Wire(grain),
            projectsScope.Count > 0 ? projectsScope : null,
            summary.Projects.Select(project => ToProject(project, grain)).ToList(),
            summary.ProjectEdges.Select(e => new GraphProjectEdgeJson(e.Source, e.Target, e.References)).ToList(),
            skeleton
                ? null
                : summary.ExternalEdges.Select(e => new GraphExternalEdgeJson(e.Source, e.TargetNamespaceRoot, e.References)).ToList(),
            skeleton ? summary.ExternalEdges.Count : null,
            multiplyDeclaredRows,
            multiplyDeclaredCount,
            shadowedRows,
            shadowedCount,
            workspaceDiagnostics.Count > 0 ? workspaceDiagnostics : null,
            trust.ModelIncomplete,
            trust.FailedProjects,
            trust.UncheckedProjects,
            trust.RestoreFailedProjects,
            trust.UnsupportedProjects);

        return JsonSerializer.Serialize(document, LoadBearingJson.Context.GraphJson);
    }

    // A coverage statement's two keys, from the one rule both take. It rides the same rung as the external
    // rows, and for the same reason: its length scales with the codebase, so a skeleton carries the count
    // instead. Where it parts company is the empty case — absent rather than an empty array or a zero count
    // — so a solution with nothing to say carries neither key at any grain, and its survey is byte-identical
    // to the one before the statement existed.
    private static (IReadOnlyList<TJson>? Rows, int? Count) CoverageRung<T, TJson>(
        IReadOnlyList<T> items, bool skeleton, Func<T, TJson> map)
    {
        if (items.Count == 0) return (null, null);
        if (skeleton) return (null, items.Count);

        List<TJson> rows = items.Select(map)
            .ToList();
        return (rows, null);
    }

    // The framework pair takes no grain argument, deliberately: it rides the project row like solutionMember
    // and the project's own generated count, so it survives every rung the roster does. Omitted when empty,
    // which is every single-framework project — the rule that keeps an ordinary solution's survey the
    // document it was before the keys existed.
    private static GraphProjectJson ToProject(ProjectSummary project, DocumentGrain grain)
    {
        return new GraphProjectJson(
            project.Name,
            project.SolutionMember,
            project.TargetFrameworks.Count > 0 ? project.TargetFrameworks : null,
            project.FactsFollow,
            project.ProjectReferences,
            project.Types,
            project.Generated > 0 ? project.Generated : null,
            grain >= DocumentGrain.Overview
                ? null
                : project.Namespaces.Select(ToNamespace).ToList());
    }

    // Omitted at zero on both keys, which is what keeps the survey of a solution with no generators exactly
    // the document it was before the key existed — and keeps the skeleton grain inside its token budget.
    private static GraphNamespaceJson ToNamespace(NamespaceCount @namespace)
    {
        return new GraphNamespaceJson(
            @namespace.Namespace, @namespace.Types, @namespace.Generated > 0 ? @namespace.Generated : null);
    }
}
