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
///     a rule that "passes" only because a project did not load is worse than no answer. The gate keys
///     strictly on the workspace-load diagnostics (<see cref="CodebaseSource.Diagnostics" />); the advisory
///     merge notes (<see cref="CodebaseSource.MergeNotes" />) render into the same diagnostics stream
///     but never gate. NuGetAudit advisories (NU19xx) also ride that stream but are carved out of the gate
///     the same way — external advisory-publication timing must not flip a deterministic verdict
///     (<see cref="NuGetAuditDiagnostics" />). Expected failures surface as <see cref="UserErrorException" />;
///     the top-level handler maps them to exit 2. Output/error writers are injected so the in-process e2e
///     tests can capture them, and the <see cref="IEnvironment" /> seam supplies the cache-root override.
/// </summary>
internal sealed class CheckRunner(
    TextWriter output,
    TextWriter error,
    ISolutionSource? source = null,
    IEnvironment? environment = null)
{
    /// <summary>
    ///     The stderr line the workspace-diagnostics gate emits before exit 2. Kept as one line, printed
    ///     after the per-project load warnings it refers to, and naming the opt-out flag.
    /// </summary>
    internal const string IncompleteModelGateMessage =
        "error: the model is incomplete — one or more projects failed to load (see the warnings above), so check "
        + "cannot pass. Pass --allow-workspace-diagnostics to check against the partial model anyway.";

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
        // never reach this gate by construction (they ride source.MergeNotes); NuGetAudit advisories (NU19xx)
        // do land in source.Diagnostics, so they are filtered out here — external advisory-publication timing
        // is time-varying noise that must not flip a deterministic gate, and it still renders above. Hoisted
        // above Render so the SARIF renderer stamps the same verdict the gate below returns.
        IReadOnlyList<string> gatingDiagnostics =
            [.. source.Diagnostics.Where(d => !NuGetAuditDiagnostics.IsAudit(d))];
        bool executionSuccessful = !(gatingDiagnostics.Count > 0 && !request.AllowWorkspaceDiagnostics);

        Render(
            request, report, source.SolutionDirectory, Path.GetFileName(source.SolutionPath),
            Path.GetFileName(source.Resolution.DllPath), renderedDiagnostics, executionSuccessful);

        // The incomplete-model gate: exit 2 overrides the 0/1 verdict. SARIF (if requested) was already
        // written above with executionSuccessful: false, so the gate verdict still reaches code scanning.
        if (!executionSuccessful)
        {
            error.WriteLine(IncompleteModelGateMessage);
            return 2;
        }

        return report.HasViolations ? 1 : 0;
    }

    private void Render(
        CheckRequest request, CheckReport report, string solutionDirectory, string solutionName, string specAssembly,
        IReadOnlyList<string> diagnostics, bool executionSuccessful)
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

        // The optional third render target: a SARIF file alongside stdout. The wrote line is human-mode only
        // (it would break --json stdout purity); the SARIF itself carries the same result model either way.
        if (request.Sarif is { } sarifPath)
        {
            SarifReportRenderer.Render(sarifPath, report, solutionDirectory, executionSuccessful, diagnostics);
            if (!request.Json) output.WriteLine($"wrote {PathFormat.Relative(solutionDirectory, sarifPath)}");
        }
    }
}