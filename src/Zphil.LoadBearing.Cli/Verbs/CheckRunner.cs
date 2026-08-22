using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Cli.Mcp.Infrastructure;
using Zphil.LoadBearing.Cli.Pipeline;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Hosting;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Diagnostics;

namespace Zphil.LoadBearing.Cli.Verbs;

/// <summary>
///     The <c>check</c> pipeline: build a <see cref="CodebaseSource" /> (cache hit or cold workspace) →
///     select the rules to run (<see cref="CheckRequest.Rules" />, everything by default) → run the shared
///     <see cref="CheckPipeline" /> (baselines, extraction, ratcheted check) → render (human
///     or JSON) → exit code (0 clean / 1 red violations; grandfathered Migrate violations do not fail
///     the run). A narrowed run is a smaller report of the same shape and the same exit contract, so its
///     verdict answers a smaller question — which is what the stamp and the document's <c>rulesFilter</c>
///     say out loud.
/// </summary>
/// <remarks>
///     <para>
///         <b>What it refuses.</b> A workspace-load failure overrides that verdict: the model is incomplete,
///         so <c>check</c> fails closed with exit 2 unless
///         <see cref="CheckRequest.AllowWorkspaceDiagnostics" /> was passed — a rule that "passes" only
///         because a project did not load is worse than no answer. That decision, its NuGetAudit carve-out,
///         and the messages the other verbs use for the same condition all live in
///         <see cref="IncompleteModelGate" />; <c>check</c> renders before it fires, so the verdict also
///         rides the JSON document and the SARIF stamp. Expected failures surface as
///         <see cref="UserErrorException" />; the top-level handler maps them to exit 2.
///     </para>
///     <para>
///         Output/error writers are injected so the in-process e2e tests can capture them, the
///         <see cref="IEnvironment" /> seam supplies the cache-root override, and the
///         <see cref="IResponseFitter" /> decides which rung of the grain ladder a caller with a response
///         budget actually gets (default: the grain they asked for).
///     </para>
/// </remarks>
internal sealed class CheckRunner(
    TextWriter output,
    TextWriter error,
    ISolutionSource? source = null,
    IEnvironment? environment = null,
    IResponseFitter? fitter = null) : CacheWiredRunner(source, environment, fitter)
{
    public async Task<int> RunAsync(CheckRequest request, CancellationToken ct)
    {
        // This run's human channel. --json owns stdout, where the document is the only thing written, so
        // under it every stamp and notice below goes nowhere.
        TextWriter human = request.Json ? TextWriter.Null : output;

        using var source = await CodebaseSource.CreateWithSpecAsync(
            SolutionSource, Environment, request.Solution, request.Spec, request.WorkingDirectory, request.NoCache, ct);

        // Select — and refuse an unmatched filter — before extraction: rule IDs come from the spec model,
        // which is already resolved here, so nothing about a bad --rules value needs a codebase walk to say
        // so. The stamp goes out just as early, because it says what the operator is about to wait for.
        IReadOnlyList<string> ruleGlobs = GlobList.Parse(request.Rules);
        IReadOnlyList<ArchRule> rules = CheckPipeline.SelectRules(source.Model, ruleGlobs);

        // All three stamps are human-channel only: under --json the document carries the same facts in
        // uncheckedProjects, unsupportedProjects and rulesFilter.
        NarrowingNotices.Stamp(human, source, NarrowedUniverseNotice.CheckStamp);
        UnsupportedProjectsNotices.Stamp(human, source, UnsupportedProjectsNotice.CheckStamp);
        WriteFilterStamp(human, ruleGlobs, rules.Count, source.Model.Rules.Count);

        CheckReport report = await CheckPipeline.ExecuteAsync(source, request.DiffBase, rules, ct);
        RecordCacheOutcome(source);

        // check is the one verb that folds the advisory merge notes into its rendered diagnostics stream
        // (stderr warning: lines + the JSON workspaceDiagnostics array + SARIF notifications), load failures
        // first. It has already extracted by the time it renders, so it is the only verb whose merge notes
        // exist yet, and the merge advisories are read here beside the violations they can
        // explain. They never reach the gate: that decision is WorkspaceDiagnostics' and keys on the load
        // failures alone.
        WorkspaceDiagnostics diagnostics = source.Diagnostics;
        IReadOnlyList<string> renderedDiagnostics = diagnostics.RenderedWithMergeNotes;

        // Fail closed on an incomplete model (a project failed to load, or to restore): a workspace-load diagnostic makes
        // exit 2 take precedence over 0/1, unless the operator opted into the partial model. The NuGetAudit
        // carve-out lives with the rest of the shared answer. Computed above Render, which reads the same
        // value, so the document and the SARIF stamp carry the verdict the gate below returns.
        bool gated = diagnostics.Gates(request.AllowWorkspaceDiagnostics);

        Render(
            request, human, report, source.SolutionDirectory, source.SolutionName,
            Path.GetFileName(source.Resolution.DllPath), renderedDiagnostics, diagnostics, !gated, ruleGlobs);

        // The incomplete-model gate: exit 2 overrides the 0/1 verdict. SARIF (if requested) was already
        // written above with executionSuccessful: false, so the gate verdict still reaches code scanning.
        if (IncompleteModelNotices.Refused(
                error, diagnostics, request.AllowWorkspaceDiagnostics, IncompleteModelGate.CheckMessage))
            return 2;

        return report.HasViolations ? 1 : 0;
    }

    // The human filter stamp, written by the runner rather than by Core's shared HumanReportRenderer so an
    // unfiltered run is byte-identical to what it always was and the xUnit adapter never sees a CLI concern.
    // It says what the report cannot: how much of the spec this verdict covers. Human-channel only — under
    // --json the document carries the same fact in rulesFilter.
    private static void WriteFilterStamp(
        TextWriter human, IReadOnlyList<string> ruleGlobs, int selected, int total)
    {
        if (ruleGlobs.Count == 0) return;

        human.WriteLine(
            $"Checking {selected} of {total} rules matching '{string.Join(";", ruleGlobs)}'; the verdict below "
            + "covers only those, so a clean result here is not a clean solution.");
        human.WriteLine();
    }

    private void Render(
        CheckRequest request, TextWriter human, CheckReport report, string solutionDirectory, string solutionName,
        string specAssembly, IReadOnlyList<string> renderedDiagnostics, WorkspaceDiagnostics diagnostics,
        bool executionSuccessful, IReadOnlyList<string> ruleGlobs)
    {
        // --json purity: only the JSON document reaches stdout; diagnostics go to stderr and ride
        // inside the document's workspaceDiagnostics array.
        WorkspaceDiagnosticsRenderer.Render(error, renderedDiagnostics, request.Json);

        if (request.Json)
            WriteJson(
                request, report, solutionDirectory, solutionName, specAssembly, renderedDiagnostics, diagnostics,
                ruleGlobs);
        else
            HumanReportRenderer.Render(output, report, solutionDirectory);

        // The optional third render target: a SARIF file alongside stdout. The wrote line is human-mode only
        // (it would break --json stdout purity); the SARIF itself carries the same result model either way.
        if (request.Sarif is { } sarifPath)
        {
            SarifReportRenderer.Render(
                sarifPath, report, solutionDirectory, executionSuccessful, renderedDiagnostics, diagnostics);
            human.WriteLine($"wrote {PathFormat.Relative(solutionDirectory, sarifPath)}");
        }
    }

    // The JSON report, degraded rather than cut. The runner offers every grain from the requested floor down
    // to the coarsest as a lazy ladder and the fitter picks; a caller whose transport has a response budget
    // gets the first rung that fits, re-composed from the result model already in hand — no second check, and
    // byte-identical to what that grain's own flag would have written, so the two surfaces cannot drift.
    // Degrading coarsens the grain and never touches the selection: every rule the run evaluated is still
    // here with its verdict, and the summary counts still cover exactly what ran.
    private void WriteJson(
        CheckRequest request, CheckReport report, string solutionDirectory, string solutionName,
        string specAssembly, IReadOnlyList<string> renderedDiagnostics, WorkspaceDiagnostics diagnostics,
        IReadOnlyList<string> ruleGlobs)
    {
        IEnumerable<string> ladder = DocumentGrains.Ladder(request.Grain, Compose);
        string document = Fitter.Fit(ladder);
        output.WriteLine(document);
        return;

        string Compose(DocumentGrain at)
        {
            return JsonReportRenderer.Document(
                report, solutionDirectory, solutionName, specAssembly, request.DiffBase, renderedDiagnostics,
                diagnostics, ruleGlobs, at);
        }
    }
}
