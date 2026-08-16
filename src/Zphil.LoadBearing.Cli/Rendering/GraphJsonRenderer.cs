using System.Text.Json;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn;

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
        bool elideExternalEdges = grain >= DocumentGrain.Skeleton;
        var relativizer = new PathFormat.Relativizer(solutionDirectory);
        WorkspaceTrustStamp trust = WorkspaceTrustStamp.From(diagnostics, relativizer);

        var document = new GraphJson(
            1,
            solutionName,
            DocumentGrains.Wire(grain),
            projectsScope.Count > 0 ? projectsScope : null,
            summary.Projects.Select(project => ToProject(project, grain)).ToList(),
            summary.ProjectEdges.Select(e => new GraphProjectEdgeJson(e.Source, e.Target, e.References)).ToList(),
            elideExternalEdges
                ? null
                : summary.ExternalEdges.Select(e => new GraphExternalEdgeJson(e.Source, e.TargetNamespaceRoot, e.References)).ToList(),
            elideExternalEdges ? summary.ExternalEdges.Count : null,
            workspaceDiagnostics.Count > 0 ? workspaceDiagnostics : null,
            trust.ModelIncomplete,
            trust.FailedProjects,
            trust.UncheckedProjects,
            trust.RestoreFailedProjects);

        return JsonSerializer.Serialize(document, LoadBearingJson.Context.GraphJson);
    }

    private static GraphProjectJson ToProject(ProjectSummary project, DocumentGrain grain)
    {
        return new GraphProjectJson(
            project.Name,
            project.SolutionMember,
            project.ProjectReferences,
            project.Types,
            grain >= DocumentGrain.Overview
                ? null
                : project.Namespaces.Select(n => new GraphNamespaceJson(n.Namespace, n.Types)).ToList());
    }
}
