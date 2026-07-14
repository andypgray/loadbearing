using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The <c>check</c> pipeline: load the model through the shared <see cref="ModelPipeline" /> → run
///     the shared <see cref="CheckPipeline" /> (baselines, extraction, ratcheted check) → render (human
///     or JSON) → exit code (0 clean / 1 red violations; grandfathered Migrate violations do not fail
///     the run). Expected failures surface as <see cref="UserErrorException" />; the top-level handler
///     maps them to exit 2. Output/error writers are injected so the in-process e2e tests can capture them.
/// </summary>
internal sealed class CheckRunner(TextWriter output, TextWriter error)
{
    public async Task<int> RunAsync(CheckRequest request, CancellationToken ct)
    {
        using WorkspaceModel workspace = await ModelPipeline.LoadWithWorkspaceAsync(
            request.Solution, request.Spec, request.WorkingDirectory, ct);

        CheckReport report = await CheckPipeline.ExecuteAsync(workspace, request.DiffBase, ct);
        Render(
            request, report, workspace.SolutionDirectory, Path.GetFileName(workspace.SolutionPath),
            Path.GetFileName(workspace.Resolution.DllPath), workspace.Diagnostics);

        return report.HasViolations ? 1 : 0;
    }

    private void Render(
        CheckRequest request, CheckReport report, string solutionDirectory, string solutionName, string specAssembly,
        IReadOnlyList<string> diagnostics)
    {
        if (request.Json)
        {
            // --json purity: only the JSON document reaches stdout; diagnostics go to stderr and ride
            // inside the document's workspaceDiagnostics array.
            foreach (string diagnostic in diagnostics) error.WriteLine(diagnostic);
            JsonReportRenderer.Render(output, report, solutionDirectory, solutionName, specAssembly, request.DiffBase, diagnostics);
        }
        else
        {
            foreach (string diagnostic in diagnostics) error.WriteLine($"warning: {diagnostic}");
            HumanReportRenderer.Render(output, report, solutionDirectory);
        }
    }
}