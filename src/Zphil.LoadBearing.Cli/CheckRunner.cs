using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Cli.Mcp.Infrastructure;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The <c>check</c> pipeline: build a <see cref="CodebaseSource" /> (cache hit or cold workspace) → run
///     the shared <see cref="CheckPipeline" /> (baselines, extraction, ratcheted check) → render (human
///     or JSON) → exit code (0 clean / 1 red violations; grandfathered Migrate violations do not fail
///     the run). A workspace-load failure overrides that verdict: the model is incomplete, so <c>check</c>
///     fails closed with exit 2 unless <see cref="CheckRequest.AllowWorkspaceDiagnostics" /> was passed —
///     a rule that "passes" only because a project did not load is worse than no answer. That decision, its
///     NuGetAudit carve-out, and the messages the other verbs use for the same condition all live in
///     <see cref="IncompleteModelGate" />; <c>check</c> renders before it fires, so the verdict also rides
///     the JSON document and the SARIF stamp. Expected failures surface as <see cref="UserErrorException" />;
///     the top-level handler maps them to exit 2. Output/error writers are injected so the in-process e2e
///     tests can capture them, and the <see cref="IEnvironment" /> seam supplies the cache-root override.
/// </summary>
internal sealed class CheckRunner(
    TextWriter output,
    TextWriter error,
    ISolutionSource? source = null,
    IEnvironment? environment = null)
{
    private readonly IEnvironment environment = environment ?? new SystemEnvironment();
    private readonly ISolutionSource solutionSource = source ?? new ColdSolutionSource();

    /// <summary>The cache path the last run took. Internal test observable; never printed.</summary>
    internal CodebaseSourceOutcome? LastOutcome { get; private set; }

    /// <summary>The projects the last run re-extracted from a workspace. Internal test observable; never printed.</summary>
    internal IReadOnlySet<string> LastReExtractedProjects { get; private set; } = new HashSet<string>();

    public async Task<int> RunAsync(CheckRequest request, CancellationToken ct)
    {
        using var source = await CodebaseSource.CreateWithSpecAsync(
            solutionSource, environment, request.Solution, request.Spec, request.WorkingDirectory, request.NoCache, ct);

        CheckReport report = await CheckPipeline.ExecuteAsync(source, request.DiffBase, ct);
        LastOutcome = source.Outcome;
        LastReExtractedProjects = source.ReExtractedProjects;

        // Workspace-load failures and merge notes ride the one rendered diagnostics stream (stderr warning:
        // lines + the JSON workspaceDiagnostics array), load failures first. Only the load failures gate,
        // so the two are combined for display but kept separate for the exit decision below.
        IReadOnlyList<string> renderedDiagnostics = [.. source.Diagnostics, .. source.MergeNotes];

        // Fail closed on an incomplete model (a project failed to load): a workspace-load diagnostic makes
        // exit 2 take precedence over 0/1, unless the operator opted into the partial model. Merge notes
        // never reach the gate by construction (they ride source.MergeNotes); the NuGetAudit carve-out lives
        // in IncompleteModelGate with the rest of the shared answer. Both computed above Render so the
        // document and the SARIF stamp carry the same verdict the gate below returns.
        bool modelIncomplete = IncompleteModelGate.IsIncomplete(source.Diagnostics);
        bool gated = IncompleteModelGate.Gates(source.Diagnostics, request.AllowWorkspaceDiagnostics);

        Render(
            request, report, source.SolutionDirectory, Path.GetFileName(source.SolutionPath),
            Path.GetFileName(source.Resolution.DllPath), renderedDiagnostics, !gated, modelIncomplete);

        // The incomplete-model gate: exit 2 overrides the 0/1 verdict. SARIF (if requested) was already
        // written above with executionSuccessful: false, so the gate verdict still reaches code scanning.
        if (gated)
        {
            error.WriteLine(IncompleteModelGate.CheckMessage);
            return 2;
        }

        return report.HasViolations ? 1 : 0;
    }

    private void Render(
        CheckRequest request, CheckReport report, string solutionDirectory, string solutionName, string specAssembly,
        IReadOnlyList<string> diagnostics, bool executionSuccessful, bool modelIncomplete)
    {
        // --json purity: only the JSON document reaches stdout; diagnostics go to stderr and ride
        // inside the document's workspaceDiagnostics array.
        WorkspaceDiagnosticsRenderer.Render(error, diagnostics, request.Json);

        if (request.Json)
            JsonReportRenderer.Render(
                output, report, solutionDirectory, solutionName, specAssembly, request.DiffBase, diagnostics, modelIncomplete);
        else
            HumanReportRenderer.Render(output, report, solutionDirectory);

        // The optional third render target: a SARIF file alongside stdout. The wrote line is human-mode only
        // (it would break --json stdout purity); the SARIF itself carries the same result model either way.
        if (request.Sarif is { } sarifPath)
        {
            SarifReportRenderer.Render(sarifPath, report, solutionDirectory, executionSuccessful, diagnostics);
            if (!request.Json) output.WriteLine($"wrote {PathFormat.Relative(solutionDirectory, sarifPath)}");
        }
    }
}
