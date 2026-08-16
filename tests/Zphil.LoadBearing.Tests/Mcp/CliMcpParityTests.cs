using System.Globalization;
using ModelContextProtocol.Protocol;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Mcp.Pipeline;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Tests.Cli;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Mcp;

/// <summary>
///     The acceptance table: every <c>arch_*</c> tool returns output identical (after newline
///     normalization) to the CLI verb it shells over — parity by construction (each tool runs the same
///     runner into a captured writer). One method per solution+spec combo; the explain rows ride the DLL
///     fast path (no workspace), <c>arch_context</c> is pinned against the render card text, and the
///     <c>diffBase</c> row mirrors <see cref="TripwireDiffE2ETests" /> over a real git repo. Serialized
///     with the watchdog suites — the filter brackets each call with the shared
///     <see cref="Zphil.LoadBearing.Cli.Mcp.Infrastructure.IdleTimeoutWatchdog" /> in-flight counter.
///     <para>
///         The narrowing rows extend the same contract to the knobs — each tool argument produces exactly
///         what its CLI option produces — and the two budget rows cover the one behaviour with no CLI
///         spelling: over the client's declared response budget, <c>arch_graph</c> and <c>arch_check</c>
///         each re-render one rung coarser, and what comes back is byte-identical to what that grain's own
///         flag writes. Each walks the ladder, not one step of it: a budget between the full and overview
///         documents returns the <c>--overview</c> document, one between overview and skeleton returns the
///         <c>--skeleton</c> document. That identity is the whole claim, because it is what makes a degraded
///         answer a complete document rather than a cut one.
///     </para>
/// </summary>
/// <remarks>
///     The CLI side of every row runs <see cref="CliRunner.InvokeColdAsync(string[])" />, not the warm-by-default
///     <see cref="CliRunner.InvokeAsync" />. The harness these rows compare against is warm, so this is the
///     suite's warm-against-cold net; serving both sides from one pooled workspace would make it compare
///     the warm path with itself.
/// </remarks>
[Collection("Serial")]
public sealed class CliMcpParityTests
{
    // The AgentContextRenderer.ScopeCard body arch_context returns for the quarantined legacy/billing scope —
    // the RenderCommandE2ETests.ScopeBody card without its provenance line (moves with that pin).
    private const string ExpectedScopeCard =
        "## Quarantined scope `legacy/billing`\n\n" +
        "This directory holds the quarantined `legacy/billing` scope. Here be dragons — do not spread references into it.\n\n" +
        "Dragons: Banker's rounding happens at line-item level, NOT invoice level. " +
        "Nightly reconciliation depends on this. Do not normalize.\n\n" +
        "- `legacy/billing/containment` — Types in `MyApp.Legacy.Billing.*`, except `IBillingFacade` or " +
        "`BillingFacade` must be referenced only by types in `MyApp.Legacy.Billing.*`, `IBillingFacade` or " +
        "`BillingFacade`. Replacement scheduled (BillingV2, ADR-019); not worth stabilizing.\n" +
        "- Sanctioned surface: `IBillingFacade`, `BillingFacade`.\n" +
        "- Expand: `loadbearing explain legacy/billing/containment`.";

    // The AgentContextRenderer.LayerCard body arch_context returns for the Web layer of MyAppLayerSpec —
    // no provenance line (that is a render file-splice concern), mirroring the quarantined-scope card above.
    private const string ExpectedWebLayerCard =
        "## Layer `Web`\n\n" +
        "This directory holds the `Web` layer. Its architecture rules:\n\n" +
        "- `layering/web-not-billing` — The Web layer must not reference types in `MyApp.Legacy.Billing.*`. " +
        "The web layer must reach billing only through the sanctioned facade.\n" +
        "- Expand any rule above with `loadbearing explain <rule-id>`.";

    // The truncator's own token → character multiple, restated here because the budget row has to work
    // backwards from a character count to the token budget a client would declare. The two preconditions it
    // asserts on the resulting cap are what keep this honest if the multiple ever moves.
    private const double CharsPerToken = 2.5;

    // The three cold graph documents this suite measures against — one per rung of the grain ladder — each
    // produced once. Several rows want more than one of them, and the solution behind them is never mutated
    // here — the diff-base row edits its own TempGitRepo copy — so the same document answers every row.
    // Memoizing the task does not warm anything: the run underneath is still CliRunner.InvokeColdAsync,
    // freshly loaded, which is what the remarks above require; it is awaited more than once instead of re-run.
    private static readonly Lazy<Task<CliResult>> ColdGraph =
        new(() => CliRunner.InvokeColdAsync("graph", CliRunner.MyAppSolution, "--json"));

    private static readonly Lazy<Task<CliResult>> ColdGraphOverview =
        new(() => CliRunner.InvokeColdAsync("graph", CliRunner.MyAppSolution, "--json", "--overview"));

    private static readonly Lazy<Task<CliResult>> ColdGraphSkeleton =
        new(() => CliRunner.InvokeColdAsync("graph", CliRunner.MyAppSolution, "--json", "--skeleton"));

    // The three cold check documents, memoized for the same reason and against the same spec every row here
    // binds to. Nothing mutates the solution behind them either — the diff-base row runs against its own
    // TempGitRepo copy — so the same full document answers the parity row and the budget row alike.
    private static readonly Lazy<Task<CliResult>> ColdCheckFull =
        new(() => CliRunner.InvokeColdAsync(
            "check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--json"));

    private static readonly Lazy<Task<CliResult>> ColdCheckOverview =
        new(() => CliRunner.InvokeColdAsync(
            "check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--json", "--overview"));

    private static readonly Lazy<Task<CliResult>> ColdCheckSkeleton =
        new(() => CliRunner.InvokeColdAsync(
            "check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--json", "--skeleton"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task HarnessA_ViolatedSpec_CheckStatusExplain_MatchCli()
    {
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.For(CliRunner.MyAppSolution, CliRunner.ViolatedSpecDll), Ct);

        // arch_check ≡ check --json (CLI exits 1 on the violation; the tool never reports IsError). The
        // ViolatedSpec carries every violation kind including the member-subject rule naming/async-suffix
        // (memberShape / subjectMember, GRAMMAR §4.6), so this byte-parity covers member subjects too.
        CliResult cliCheck = await ColdCheckFull.Value;
        cliCheck.ShouldReportViolations();
        CallToolResult mcpCheck = await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct);
        mcpCheck.IsError.ShouldNotBe(true);
        mcpCheck.ShouldHaveTextContent()
            .NormalizedTrimmed()
            .ShouldBe(cliCheck.Out.NormalizedTrimmed());

        // arch_status ≡ status --json.
        CliResult cliStatus = await CliRunner.InvokeColdAsync(
            "status", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--json");
        CallToolResult mcpStatus = await harness.Client.CallToolAsync("arch_status", cancellationToken: Ct);
        mcpStatus.ShouldHaveTextContent()
            .NormalizedTrimmed()
            .ShouldBe(cliStatus.Out.NormalizedTrimmed());

        // arch_graph ≡ graph --json (spec-independent; the survey ignores the bound spec, and graph takes no --spec).
        CliResult cliGraph = await ColdGraph.Value;
        CallToolResult mcpGraph = await harness.Client.CallToolAsync("arch_graph", cancellationToken: Ct);
        mcpGraph.ShouldHaveTextContent()
            .NormalizedTrimmed()
            .ShouldBe(cliGraph.Out.NormalizedTrimmed());

        // arch_explain <known> ≡ explain <known> stdout.
        CliResult cliExplain = await CliRunner.InvokeColdAsync(
            "explain", "layering/domain-independent", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);
        CallToolResult mcpExplain = await harness.Client.CallToolAsync(
            "arch_explain", new Dictionary<string, object?> { ["ruleId"] = "layering/domain-independent" }, cancellationToken: Ct);
        mcpExplain.ShouldHaveTextContent()
            .NormalizedTrimmed()
            .ShouldBe(cliExplain.Out.NormalizedTrimmed());

        // arch_explain <unknown> IsError text ≡ explain <unknown> stderr (CLI exit 2).
        CliResult cliUnknown = await CliRunner.InvokeColdAsync(
            "explain", "unknown/id", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);
        cliUnknown.ShouldRefuseWith();
        CallToolResult mcpUnknown = await harness.Client.CallToolAsync(
            "arch_explain", new Dictionary<string, object?> { ["ruleId"] = "unknown/id" }, cancellationToken: Ct);
        mcpUnknown.IsError.ShouldBe(true);
        mcpUnknown.ShouldHaveTextContent()
            .NormalizedTrimmed()
            .ShouldBe(cliUnknown.Err.NormalizedTrimmed());
    }

    [Fact]
    public async Task HarnessB_CleanSpec_Check_MatchesCli()
    {
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.For(CliRunner.MyAppSolution, CliRunner.CleanSpecDll), Ct);

        CliResult cliCheck = await CliRunner.InvokeColdAsync(
            "check", CliRunner.MyAppSolution, "--spec", CliRunner.CleanSpecDll, "--json");
        cliCheck.ShouldSucceed();
        CallToolResult mcpCheck = await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct);

        mcpCheck.ShouldHaveTextContent()
            .NormalizedTrimmed()
            .ShouldBe(cliCheck.Out.NormalizedTrimmed());
    }

    [Fact]
    public async Task HarnessC_RenderSpec_Context_InScopeCardAndOutOfScopePointer()
    {
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.For(CliRunner.MyAppSolution, CliRunner.RenderSpecDll), Ct);

        // A path inside the quarantined scope → that scope's card body.
        CallToolResult inScope = await harness.Client.CallToolAsync(
            "arch_context", new Dictionary<string, object?> { ["path"] = "MyApp.Legacy.Billing/BillingCalculator.cs" }, cancellationToken: Ct);
        inScope.ShouldHaveTextContent()
            .NormalizedTrimmed()
            .ShouldBe(ExpectedScopeCard);

        // A path no scope covers → the pinned pointer line (echoing the query path). The RenderSpec's
        // Domain/Web layers carry no anchored rules, so no layer card competes here.
        CallToolResult outScope = await harness.Client.CallToolAsync(
            "arch_context", new Dictionary<string, object?> { ["path"] = "MyApp.Web/HomeController.cs" }, cancellationToken: Ct);
        outScope.ShouldHaveTextContent()
            .NormalizedTrimmed()
            .ShouldBe(
                "No architecture scope covers 'MyApp.Web/HomeController.cs'. Architecture context for this solution lives in " +
                "the root AGENTS.md managed block; expand any rule with 'loadbearing explain <rule-id>'.");
    }

    [Fact]
    public async Task HarnessD_QuarantinedSpec_CheckDiffBase_MatchesCliAndWarnsTripwire()
    {
        using var repo = new TempGitRepo();
        // A brand-new untracked file in dragon territory — the tripwire's agent-hook case.
        File.WriteAllText(
            repo.PathOf("MyApp.Legacy.Billing", "LegacyNote.cs"),
            "namespace MyApp.Legacy.Billing;\n\npublic class LegacyNote;\n");

        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.For(repo.SolutionPath, CliRunner.QuarantinedSpecDll), Ct);

        CliResult cliCheck = await CliRunner.InvokeColdAsync(
            "check", repo.SolutionPath, "--spec", CliRunner.QuarantinedSpecDll, "--json", "--diff-base", "HEAD");
        CallToolResult mcpCheck = await harness.Client.CallToolAsync(
            "arch_check", new Dictionary<string, object?> { ["diffBase"] = "HEAD" }, cancellationToken: Ct);

        string mcpText = mcpCheck.ShouldHaveTextContent()
            .NormalizedTrimmed();
        mcpText.ShouldBe(cliCheck.Out.NormalizedTrimmed());
        mcpText.ShouldContain("quarantinedScopeTouched");
    }

    [Fact]
    public async Task HarnessE_LayerSpec_Context_InLayerCardAndOutOfScopePointer()
    {
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.For(CliRunner.MyAppSolution, CliRunner.LayerSpecDll), Ct);

        // A path inside the Web layer directory → that layer's local-rules card.
        CallToolResult inLayer = await harness.Client.CallToolAsync(
            "arch_context", new Dictionary<string, object?> { ["path"] = "MyApp.Web/HomeController.cs" }, cancellationToken: Ct);
        inLayer.ShouldHaveTextContent()
            .NormalizedTrimmed()
            .ShouldBe(ExpectedWebLayerCard);

        // A path no layer or quarantined scope covers → the reworded pointer line (echoing the query path).
        CallToolResult outScope = await harness.Client.CallToolAsync(
            "arch_context", new Dictionary<string, object?> { ["path"] = "MyApp.Domain/Order.cs" }, cancellationToken: Ct);
        outScope.ShouldHaveTextContent()
            .NormalizedTrimmed()
            .ShouldBe(
                "No architecture scope covers 'MyApp.Domain/Order.cs'. Architecture context for this solution lives in " +
                "the root AGENTS.md managed block; expand any rule with 'loadbearing explain <rule-id>'.");
    }

    [Fact]
    public async Task HarnessF_NarrowedCalls_MatchTheirCliTwins()
    {
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.For(CliRunner.MyAppSolution, CliRunner.ViolatedSpecDll), Ct);

        // arch_graph overview ≡ graph --overview --json: the whole survey at coarser grain.
        CliResult cliOverview = await ColdGraphOverview.Value;
        CallToolResult mcpOverview = await harness.Client.CallToolAsync(
            "arch_graph", new Dictionary<string, object?> { ["overview"] = true }, cancellationToken: Ct);
        mcpOverview.ShouldHaveTextContent()
            .NormalizedTrimmed()
            .ShouldBe(cliOverview.Out.NormalizedTrimmed());

        // arch_graph skeleton ≡ graph --skeleton --json: the structural spine, external rows as a count.
        CliResult cliSkeleton = await ColdGraphSkeleton.Value;
        CallToolResult mcpSkeleton = await harness.Client.CallToolAsync(
            "arch_graph", new Dictionary<string, object?> { ["skeleton"] = true }, cancellationToken: Ct);
        mcpSkeleton.ShouldHaveTextContent()
            .NormalizedTrimmed()
            .ShouldBe(cliSkeleton.Out.NormalizedTrimmed());

        // arch_graph projects ≡ graph --projects --json: the same survey over fewer projects.
        CliResult cliScoped = await CliRunner.InvokeColdAsync(
            "graph", CliRunner.MyAppSolution, "--json", "--projects", "MyApp.Web");
        CallToolResult mcpScoped = await harness.Client.CallToolAsync(
            "arch_graph", new Dictionary<string, object?> { ["projects"] = "MyApp.Web" }, cancellationToken: Ct);
        mcpScoped.ShouldHaveTextContent()
            .NormalizedTrimmed()
            .ShouldBe(cliScoped.Out.NormalizedTrimmed());

        // arch_check rules ≡ check --rules --json (CLI exits 1 on the subset's violations; the tool never
        // reports IsError). Same parse, same selection, same document — including rulesFilter.
        CliResult cliRules = await CliRunner.InvokeColdAsync(
            "check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--json", "--rules", "exceptions/*");
        cliRules.ShouldReportViolations();
        CallToolResult mcpRules = await harness.Client.CallToolAsync(
            "arch_check", new Dictionary<string, object?> { ["rules"] = "exceptions/*" }, cancellationToken: Ct);
        mcpRules.IsError.ShouldNotBe(true);
        mcpRules.ShouldHaveTextContent()
            .NormalizedTrimmed()
            .ShouldBe(cliRules.Out.NormalizedTrimmed());

        // arch_check overview ≡ check --overview --json: the whole report with each violation's sites as a
        // count.
        CliResult cliCheckOverview = await ColdCheckOverview.Value;
        CallToolResult mcpCheckOverview = await harness.Client.CallToolAsync(
            "arch_check", new Dictionary<string, object?> { ["overview"] = true }, cancellationToken: Ct);
        mcpCheckOverview.ShouldHaveTextContent()
            .NormalizedTrimmed()
            .ShouldBe(cliCheckOverview.Out.NormalizedTrimmed());

        // arch_check skeleton ≡ check --skeleton --json: the verdict alone, violations as a count.
        CliResult cliCheckSkeleton = await ColdCheckSkeleton.Value;
        CallToolResult mcpCheckSkeleton = await harness.Client.CallToolAsync(
            "arch_check", new Dictionary<string, object?> { ["skeleton"] = true }, cancellationToken: Ct);
        mcpCheckSkeleton.ShouldHaveTextContent()
            .NormalizedTrimmed()
            .ShouldBe(cliCheckSkeleton.Out.NormalizedTrimmed());
    }

    [Fact]
    public async Task HarnessG_GraphOverTheResponseBudget_ReturnsExactlyTheCliDocumentForTheGrainItLandsOn()
    {
        await ShouldAnswerEachRungWithThatGrainsCliDocumentAsync(
            "arch_graph", ColdGraph, ColdGraphOverview, ColdGraphSkeleton, result => result.ShouldSucceed());
    }

    [Fact]
    public async Task HarnessH_CheckOverTheResponseBudget_ReturnsExactlyTheCliDocumentForTheGrainItLandsOn()
    {
        // HarnessG's twin, and the row that matters most of the two: arch_check is the tool agents are told
        // to call before finishing work, and its bulk driver is an uncapped per-site dump, so this is the
        // response most likely to overrun on the codebases the product is for. A cut report there evicts an
        // agent from the tool surface entirely, which is the failure the ladder exists for.
        await ShouldAnswerEachRungWithThatGrainsCliDocumentAsync(
            "arch_check",
            ColdCheckFull,
            ColdCheckOverview,
            ColdCheckSkeleton,
            result => result.ShouldReportViolations());
    }

    /// <summary>
    ///     Walks <paramref name="tool" /> down the grain ladder a rung at a time and asserts that each answer
    ///     is byte-identical to the CLI document for the grain it landed on: over a budget between
    ///     <paramref name="full" /> and <paramref name="overview" /> the tool must return the whole overview
    ///     document, and over one between <paramref name="overview" /> and <paramref name="skeleton" /> the
    ///     whole skeleton — nothing cut, so the JSON still parses and the client reads a whole answer rather
    ///     than half of one. <paramref name="verdict" /> is the exit contract every CLI leg must meet.
    /// </summary>
    /// <remarks>
    ///     Both budgets are derived from the CLI documents either side of the rung under test rather than
    ///     guessed, so this proves the degrade instead of assuming a fixture size. The two calls share one
    ///     harness on purpose: the tool re-reads the budget per call, so a tighter one takes effect without a
    ///     second server, and neither call names a grain — the degrade is the server's own decision.
    /// </remarks>
    private static async Task ShouldAnswerEachRungWithThatGrainsCliDocumentAsync(
        string tool,
        Lazy<Task<CliResult>> full,
        Lazy<Task<CliResult>> overview,
        Lazy<Task<CliResult>> skeleton,
        Action<CliResult> verdict)
    {
        // Arrange
        CliResult cliFull = await full.Value;
        CliResult cliOverview = await overview.Value;
        CliResult cliSkeleton = await skeleton.Value;
        verdict(cliFull);
        verdict(cliOverview);
        verdict(cliSkeleton);

        int tokens = ShouldHaveBudgetBetween(cliFull, cliOverview);

        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.For(CliRunner.MyAppSolution, CliRunner.ViolatedSpecDll), Ct);
        harness.Environment.SetVariable(
            LoadBearingEnvVars.MaxMcpOutputTokens, tokens.ToString(CultureInfo.InvariantCulture));

        // Act — the plain call, with no grain argument.
        CallToolResult mcpOverview = await harness.Client.CallToolAsync(tool, cancellationToken: Ct);

        // Assert — a complete document at coarser grain, byte-identical to what --overview writes.
        string text = mcpOverview.ShouldHaveTextContent();
        text.ShouldNotContain("--- RESPONSE TRUNCATED ---");
        text.NormalizedTrimmed()
            .ShouldBe(cliOverview.Out.NormalizedTrimmed());

        // The rung below, on the same harness: the claim is ladder-wide, not overview-shaped — what comes
        // back is whatever grain the response landed on, spelled exactly as that grain's own flag spells it.
        int skeletonTokens = ShouldHaveBudgetBetween(cliOverview, cliSkeleton);

        harness.Environment.SetVariable(
            LoadBearingEnvVars.MaxMcpOutputTokens, skeletonTokens.ToString(CultureInfo.InvariantCulture));
        CallToolResult mcpSkeleton = await harness.Client.CallToolAsync(tool, cancellationToken: Ct);

        string skeletonText = mcpSkeleton.ShouldHaveTextContent();
        skeletonText.ShouldNotContain("--- RESPONSE TRUNCATED ---");
        skeletonText.NormalizedTrimmed()
            .ShouldBe(cliSkeleton.Out.NormalizedTrimmed());
    }

    /// <summary>
    ///     The client-declared token budget that lands between <paramref name="larger" /> and
    ///     <paramref name="smaller" />, asserting that the cap it converts to really does sit at or above the
    ///     document that should survive the truncator whole and below the one that should overrun.
    /// </summary>
    private static int ShouldHaveBudgetBetween(CliResult larger, CliResult smaller)
    {
        int largerChars = larger.Out.TrimEnd('\r', '\n')
            .Length;
        int smallerChars = smaller.Out.TrimEnd('\r', '\n')
            .Length;
        var tokens = (int)Math.Ceiling((largerChars + smallerChars) / 2.0 / CharsPerToken);
        int budget = ResponseTruncator.ComputeMaxChars(tokens.ToString(CultureInfo.InvariantCulture));

        budget.ShouldBeGreaterThanOrEqualTo(smallerChars + Environment.NewLine.Length);
        budget.ShouldBeLessThan(largerChars);

        return tokens;
    }
}
