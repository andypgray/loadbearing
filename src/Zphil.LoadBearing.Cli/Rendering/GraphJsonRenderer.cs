using System.Text.Json;
using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     Renders a <see cref="GraphSummary" /> as the <c>graph --json</c> document (its own schemaVersion 1)
///     — the pre-spec codebase survey. Uses the shared <see cref="LoadBearingJson.Options" />;
///     machine-independent (<c>solution</c> is a file name). Grouped counts only, no per-site dumps.
///     <para>
///         Composing the document and writing it are separate calls so a caller with a response budget can
///         measure the full survey and, if it overruns, re-compose it at overview grain from the same
///         summary — one extraction, two renders, and never a document cut mid-array.
///     </para>
/// </summary>
internal static class GraphJsonRenderer
{
    /// <summary>
    ///     The wire spelling of each coarsened grain. <see cref="GraphGrain.Full" /> has none on purpose: a
    ///     document that says nothing about grain is the complete one.
    /// </summary>
    private static readonly Dictionary<GraphGrain, string> GrainNames = new()
    {
        [GraphGrain.Overview] = "overview",
        [GraphGrain.Skeleton] = "skeleton"
    };

    /// <summary>
    ///     The survey document as a string. <paramref name="grain" /> decides how much of each project is
    ///     rendered and stamps itself on the document; <paramref name="projectsScope" /> is the filter the
    ///     summary was already narrowed by, recorded so the document says what it covers.
    /// </summary>
    public static string Document(
        GraphSummary summary,
        string solutionName,
        IReadOnlyList<string> workspaceDiagnostics,
        bool modelIncomplete,
        GraphGrain grain,
        IReadOnlyList<string> projectsScope)
    {
        bool elideExternalEdges = grain >= GraphGrain.Skeleton;

        var document = new GraphJson(
            1,
            solutionName,
            GrainNames.GetValueOrDefault(grain),
            projectsScope.Count > 0 ? projectsScope : null,
            summary.Projects.Select(project => ToProject(project, grain)).ToList(),
            summary.ProjectEdges.Select(e => new GraphProjectEdgeJson(e.Source, e.Target, e.References)).ToList(),
            elideExternalEdges
                ? null
                : summary.ExternalEdges.Select(e => new GraphExternalEdgeJson(e.Source, e.TargetNamespaceRoot, e.References)).ToList(),
            elideExternalEdges ? summary.ExternalEdges.Count : null,
            workspaceDiagnostics.Count > 0 ? workspaceDiagnostics : null,
            modelIncomplete ? true : null);

        return JsonSerializer.Serialize(document, LoadBearingJson.Context.GraphJson);
    }

    /// <summary>Writes a composed document as the run's stdout line.</summary>
    public static void Render(TextWriter output, string document)
    {
        output.WriteLine(document);
    }

    private static GraphProjectJson ToProject(ProjectSummary project, GraphGrain grain)
    {
        return new GraphProjectJson(
            project.Name,
            project.ProjectReferences,
            project.Types,
            grain >= GraphGrain.Overview
                ? null
                : project.Namespaces.Select(n => new GraphNamespaceJson(n.Namespace, n.Types)).ToList());
    }
}
