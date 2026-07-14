using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The <c>graph</c> pipeline: discover the solution → load the workspace → extract the whole codebase
///     (no project exclusions, no spec) → summarize → render the survey (human or JSON). Deliberately
///     spec-free: the survey is a property of the codebase, and derive runs before any spec exists — so
///     unlike the other verbs there is no <see cref="ModelPipeline" /> spec resolution here, only the
///     shared solution discovery and workspace load. Reports rather than gates: it exits 0 on success
///     regardless of what the codebase contains; a discovery/workspace failure surfaces as a
///     <see cref="UserErrorException" /> the top-level handler maps to exit 2. Output/error writers are
///     injected so the in-process e2e tests can capture them.
/// </summary>
internal sealed class GraphRunner(TextWriter output, TextWriter error)
{
    public async Task<int> RunAsync(GraphRequest request, CancellationToken ct)
    {
        string solutionPath = ModelPipeline.DiscoverSolution(request.Solution, request.WorkingDirectory);
        var diagnostics = new List<string>();
        using LoadedSolution loaded = await WorkspaceLoader.LoadAsync(solutionPath, diagnostics.Add, ct);

        CodebaseModel codebase = await CodebaseExtractor.ExtractFromSolutionAsync(loaded.Solution, null, ct);
        GraphSummary summary = GraphSummarizer.Summarize(codebase);
        string solutionName = Path.GetFileName(solutionPath);

        if (request.Json)
        {
            // --json purity: only the JSON document reaches stdout; workspace diagnostics go to stderr.
            foreach (string diagnostic in diagnostics) error.WriteLine(diagnostic);
            GraphJsonRenderer.Render(output, summary, solutionName);
        }
        else
        {
            foreach (string diagnostic in diagnostics) error.WriteLine($"warning: {diagnostic}");
            foreach (string line in GraphFormatter.Lines(summary, solutionName)) output.WriteLine(line);
        }

        return 0;
    }
}