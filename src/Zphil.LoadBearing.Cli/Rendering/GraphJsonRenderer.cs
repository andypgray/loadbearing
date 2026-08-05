using System.Text.Json;
using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     Renders a <see cref="GraphSummary" /> as the <c>graph --json</c> document (its own schemaVersion 1)
///     — the pre-spec codebase survey. Uses the shared <see cref="LoadBearingJson.Options" />;
///     machine-independent (<c>solution</c> is a file name). Grouped counts only, no per-site dumps.
/// </summary>
internal static class GraphJsonRenderer
{
    public static void Render(
        TextWriter output,
        GraphSummary summary,
        string solutionName,
        IReadOnlyList<string> workspaceDiagnostics,
        bool modelIncomplete)
    {
        var document = new GraphJson(
            1,
            solutionName,
            summary.Projects.Select(ToProject).ToList(),
            summary.ProjectEdges.Select(e => new GraphProjectEdgeJson(e.Source, e.Target, e.References)).ToList(),
            summary.ExternalEdges.Select(e => new GraphExternalEdgeJson(e.Source, e.TargetNamespaceRoot, e.References)).ToList(),
            workspaceDiagnostics.Count > 0 ? workspaceDiagnostics : null,
            modelIncomplete ? true : null);

        output.WriteLine(JsonSerializer.Serialize(document, LoadBearingJson.Context.GraphJson));
    }

    private static GraphProjectJson ToProject(ProjectSummary project)
    {
        return new GraphProjectJson(
            project.Name,
            project.ProjectReferences,
            project.Types,
            project.Namespaces.Select(n => new GraphNamespaceJson(n.Namespace, n.Types)).ToList());
    }
}