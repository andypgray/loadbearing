using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     Renders a <see cref="GraphSummary" /> as the <c>graph --json</c> document (its own schemaVersion 1)
///     — the pre-spec codebase survey. Same serializer options as <see cref="JsonReportRenderer" />;
///     machine-independent (<c>solution</c> is a file name). Grouped counts only, no per-site dumps.
/// </summary>
internal static class GraphJsonRenderer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void Render(TextWriter output, GraphSummary summary, string solutionName)
    {
        var document = new GraphJson(
            1,
            solutionName,
            summary.Projects.Select(ToProject).ToList(),
            summary.ProjectEdges.Select(e => new GraphProjectEdgeJson(e.Source, e.Target, e.References)).ToList(),
            summary.ExternalEdges.Select(e => new GraphExternalEdgeJson(e.Source, e.TargetNamespaceRoot, e.References)).ToList());

        output.WriteLine(JsonSerializer.Serialize(document, Options));
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