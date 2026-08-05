using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Cli.Mcp.Infrastructure;
using Zphil.LoadBearing.Cli.Rendering;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The <c>status</c> pipeline: build a <see cref="CodebaseSource" /> (cache hit or cold workspace) → run
///     the shared <see cref="CheckPipeline" /> (baselines, extraction, ratcheted check) → render the burndown
///     (human or JSON). Unlike <c>check</c>, status <em>reports</em> — it exits 0 even with red rules; only an
///     error (a tampered baseline, an unresolvable spec) exits 2 via the top-level handler. The one thing it
///     does gate on is the model not being the codebase: a project that failed to load declares no types, so
///     every burndown count reads low and a ratchet read against it looks like progress that never happened.
///     That fails closed on <c>check</c>'s terms — the burndown still renders, then exit 2, opt-out
///     <see cref="StatusRequest.AllowWorkspaceDiagnostics" /> (<see cref="IncompleteModelGate" />). Output/error
///     writers are injected for the in-process e2e tests, and the <see cref="IEnvironment" /> seam supplies
///     the cache-root override.
/// </summary>
internal sealed class StatusRunner(
    TextWriter output,
    TextWriter error,
    ISolutionSource? source = null,
    IEnvironment? environment = null)
{
    private readonly IEnvironment environment = environment ?? new SystemEnvironment();
    private readonly ISolutionSource solutionSource = source ?? new ColdSolutionSource();

    // ReSharper disable UnusedAutoPropertyAccessor.Global — observables kept symmetric across the three cache-wired runners
    /// <summary>The cache path the last run took. Internal test observable; never printed.</summary>
    internal CodebaseSourceOutcome? LastOutcome { get; private set; }

    /// <summary>The projects the last run re-extracted from a workspace. Internal test observable; never printed.</summary>
    internal IReadOnlySet<string> LastReExtractedProjects { get; private set; } = new HashSet<string>();

    // ReSharper restore UnusedAutoPropertyAccessor.Global

    public async Task<int> RunAsync(StatusRequest request, CancellationToken ct)
    {
        using var source = await CodebaseSource.CreateWithSpecAsync(
            solutionSource, environment, request.Solution, request.Spec, request.WorkingDirectory, request.NoCache, ct);

        CheckReport report = await CheckPipeline.ExecuteAsync(source, null, ct);
        LastOutcome = source.Outcome;
        LastReExtractedProjects = source.ReExtractedProjects;

        WorkspaceDiagnosticsRenderer.Render(error, source.Diagnostics, request.Json);

        // check's shape: render the burndown it does have, stamping the verdict into the document, then gate.
        bool modelIncomplete = IncompleteModelGate.IsIncomplete(source.Diagnostics);

        if (request.Json)
            StatusJsonRenderer.Render(
                output, report, Path.GetFileName(source.SolutionPath), Path.GetFileName(source.Resolution.DllPath),
                source.Diagnostics, modelIncomplete);
        else
            foreach (string line in StatusFormatter.Lines(report))
                output.WriteLine(line);

        if (IncompleteModelGate.Gates(source.Diagnostics, request.AllowWorkspaceDiagnostics))
        {
            error.WriteLine(IncompleteModelGate.StatusMessage);
            return 2;
        }

        return 0;
    }
}