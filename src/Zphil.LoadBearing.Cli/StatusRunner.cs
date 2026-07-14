using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Cli.Rendering;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The <c>status</c> pipeline: load the model → run the shared <see cref="CheckPipeline" /> (baselines,
///     extraction, ratcheted check) → render the burndown (human or JSON). Unlike <c>check</c>, status
///     <em>reports</em> — it exits 0 even with red rules; only an error (a tampered baseline, an
///     unresolvable spec) exits 2 via the top-level handler. Output/error writers are injected for the
///     in-process e2e tests.
/// </summary>
internal sealed class StatusRunner(TextWriter output, TextWriter error)
{
    public async Task<int> RunAsync(StatusRequest request, CancellationToken ct)
    {
        using WorkspaceModel workspace = await ModelPipeline.LoadWithWorkspaceAsync(
            request.Solution, request.Spec, request.WorkingDirectory, ct);

        CheckReport report = await CheckPipeline.ExecuteAsync(workspace, null, ct);

        if (request.Json)
        {
            foreach (string diagnostic in workspace.Diagnostics) error.WriteLine(diagnostic);
            StatusJsonRenderer.Render(
                output, report, Path.GetFileName(workspace.SolutionPath), Path.GetFileName(workspace.Resolution.DllPath));
        }
        else
        {
            foreach (string diagnostic in workspace.Diagnostics) error.WriteLine($"warning: {diagnostic}");
            foreach (string line in StatusFormatter.Lines(report)) output.WriteLine(line);
        }

        return 0;
    }
}