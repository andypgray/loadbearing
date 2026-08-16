using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Cli.Mcp.Infrastructure;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The <c>status</c> pipeline: build a <see cref="CodebaseSource" /> (cache hit or cold workspace) → run
///     the shared <see cref="CheckPipeline" /> (baselines, extraction, ratcheted check) → render the burndown
///     (human or JSON). Unlike <c>check</c>, status <em>reports</em> — it exits 0 even with red rules; only an
///     error (a tampered baseline, an unresolvable spec) exits 2 via the top-level handler.
/// </summary>
/// <remarks>
///     <para>
///         <b>What it refuses.</b> The one thing status gates on is the model not being the codebase: a
///         project that failed to load declares no types, so every burndown count reads low and a ratchet
///         read against it looks like progress that never happened. That fails closed on <c>check</c>'s
///         terms — the burndown still renders, then exit 2, opt-out
///         <see cref="StatusRequest.AllowWorkspaceDiagnostics" /> (<see cref="IncompleteModelGate" />).
///     </para>
///     <para>
///         Output/error writers are injected for the in-process e2e tests, and the
///         <see cref="IEnvironment" /> seam supplies the cache-root override.
///     </para>
/// </remarks>
internal sealed class StatusRunner(
    TextWriter output,
    TextWriter error,
    ISolutionSource? source = null,
    IEnvironment? environment = null) : CacheWiredRunner(source, environment)
{
    public async Task<int> RunAsync(StatusRequest request, CancellationToken ct)
    {
        using var source = await CodebaseSource.CreateWithSpecAsync(
            SolutionSource, Environment, request.Solution, request.Spec, request.WorkingDirectory, request.NoCache, ct);

        // status carries no --rules flag: a burndown of part of the spec would read as progress on all of
        // it. The empty selection — every rule — is spelled out so both callers of the shared pipeline
        // choose their rules the same way.
        var rules = CheckPipeline.SelectRules(source.Model, []);

        CheckReport report = await CheckPipeline.ExecuteAsync(source, null, rules, ct);
        RecordCacheOutcome(source);

        // Composed once and handed to both surfaces, so the MSBuild-selection note rides the burndown
        // document as well as stderr.
        WorkspaceDiagnostics diagnostics = source.Diagnostics;
        var renderedDiagnostics = diagnostics.Rendered;
        WorkspaceDiagnosticsRenderer.Render(error, renderedDiagnostics, request.Json);
        WriteNarrowingStamp(request, source, diagnostics);

        // check's shape: render the burndown it does have, stamping the verdict into the document, then gate.
        bool modelIncomplete = diagnostics.IsIncomplete;

        if (request.Json)
            StatusJsonRenderer.Render(
                output, report, source.SolutionDirectory, Path.GetFileName(source.SolutionPath),
                Path.GetFileName(source.Resolution.DllPath), renderedDiagnostics, modelIncomplete,
                diagnostics.FailedProjects, diagnostics.UncheckedProjects);
        else
            foreach (string line in StatusFormatter.Lines(report))
                output.WriteLine(line);

        if (diagnostics.Gates(request.AllowWorkspaceDiagnostics))
        {
            foreach (string line in IncompleteModelGate.StatusMessage(diagnostics).Split('\n'))
                error.WriteLine(line);
            return 2;
        }

        return 0;
    }

    // The human narrowing stamp, byte-silent on every run that narrowed nothing and suppressed under --json,
    // where the document carries the same fact in uncheckedProjects. Sited in the runner for the rules-filter
    // stamp's reason: an unfiltered run's output stays byte-identical to what it always was.
    private void WriteNarrowingStamp(StatusRequest request, CodebaseSource source, WorkspaceDiagnostics diagnostics)
    {
        if (diagnostics.UncheckedProjects.Count == 0 || request.Json) return;

        NarrowedUniverseNotice.Write(
            output,
            NarrowedUniverseNotice.StatusStamp(
                Path.GetFileName(source.SolutionPath),
                NarrowedUniverseNotice.Relative(diagnostics.UncheckedProjects, source.SolutionDirectory)));
    }
}
