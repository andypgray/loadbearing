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
///     budget can measure the full survey and, if it overruns, re-compose it a rung coarser from the same
///     summary — one extraction, one render per rung walked, and never a document cut mid-array.
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
        bool index = grain >= DocumentGrain.Index;

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
            index
                ? null
                : summary.ProjectEdges.Select(e => new GraphProjectEdgeJson(e.Source, e.Target, e.References)).ToList(),
            index ? summary.ProjectEdges.Count : null,
            skeleton
                ? null
                : summary.ExternalEdges.Select(e => new GraphExternalEdgeJson(e.Source, e.TargetNamespaceRoot, e.References)).ToList(),
            skeleton ? summary.ExternalEdges.Count : null,
            multiplyDeclaredRows,
            multiplyDeclaredCount,
            shadowedRows,
            shadowedCount,
            index || workspaceDiagnostics.Count == 0 ? null : workspaceDiagnostics,
            index && workspaceDiagnostics.Count > 0 ? workspaceDiagnostics.Count : null,
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

    // The row's own ladder. What a project targets, whether it packs and whether it locks all ride it like
    // solutionMember and the generated count, so they survive every rung the roster does down to index,
    // where the row is cut to what names and sizes a project. Each is omitted where it has no answer, which
    // for the frameworks means no evaluation happened at all. The declared project references are the reason
    // index exists: they scale with the solution's edges rather than its projects, so on a large solution
    // they are most of the roster's bulk. The declared packages leave a rung earlier, at skeleton, beside
    // the external-reference rows they are the build-side twin of. Both elide bare, as the namespaces do at
    // overview — a row's array needs no count of its own when the document already stamps the grain that
    // dropped it.
    private static GraphProjectJson ToProject(ProjectSummary project, DocumentGrain grain)
    {
        bool index = grain >= DocumentGrain.Index;

        return new GraphProjectJson(
            project.Name,
            project.SolutionMember,
            !index && project.TargetFrameworks.Count > 0 ? project.TargetFrameworks : null,
            index ? null : project.FactsFollow,
            index ? null : project.IsPackable,
            index ? null : project.LocksPackages,
            index ? null : project.ProjectReferences,
            grain >= DocumentGrain.Skeleton ? null : project.PackageReferences,
            project.Types,
            LoadBearingJson.OmitZero(project.Generated),
            grain >= DocumentGrain.Overview
                ? null
                : project.Namespaces.Select(ToNamespace).ToList());
    }

    // Omitted at zero on both keys, which is what keeps the survey of a solution with no generators exactly
    // the document it was before the key existed — and keeps the skeleton grain inside its token budget.
    private static GraphNamespaceJson ToNamespace(NamespaceCount @namespace)
    {
        return new GraphNamespaceJson(
            @namespace.Namespace, @namespace.Types, LoadBearingJson.OmitZero(@namespace.Generated));
    }
}
