using System.Globalization;
using ModelContextProtocol.Protocol;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Mcp.Pipeline;
using Zphil.LoadBearing.Roslyn.Hosting;
using Zphil.LoadBearing.Tests.Cli;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Mcp;

/// <summary>
///     The acceptance table: every <c>arch_*</c> tool returns output identical (after newline
///     normalization) to the CLI verb it shells over — parity by construction (each tool runs the same
///     runner into a captured writer). One method per solution+spec combo; the explain rows ride the DLL
///     fast path (no workspace), <c>arch_context</c> is pinned against the render card text, and the
///     <c>diffBase</c> row mirrors <see cref="TripwireDiffE2ETests" /> over a real git repo.
/// </summary>
/// <remarks>
///     <para>
///         Serialized with the watchdog suites — the filter brackets each call with the shared
///         <see cref="Zphil.LoadBearing.Cli.Mcp.Infrastructure.IdleTimeoutWatchdog" /> in-flight counter.
///     </para>
///     <para>
///         The narrowing rows extend the same contract to the knobs — each tool argument produces exactly
///         what its CLI option produces — and the two budget rows cover the one behaviour with no CLI
///         spelling: over the client's declared response budget, <c>arch_graph</c> and <c>arch_check</c>
///         each re-render one rung coarser, and what comes back is byte-identical to what that grain's own
///         flag writes. Each walks the whole ladder, not one step of it: a budget between any two adjacent
///         rungs returns the coarser one's own document, down to the floor. That identity is the whole
///         claim, because it is what makes a degraded answer a complete document rather than a cut one.
///     </para>
///     <para>
///         The CLI side of every row runs <see cref="CliRunner.InvokeColdAsync(string[])" />, not the warm-by-default
///         <see cref="CliRunner.InvokeAsync" />. The harness these rows compare against is warm, so this is
///         the suite's warm-against-cold net; serving both sides from one pooled workspace would make it
///         compare the warm path with itself.
///     </para>
/// </remarks>
[Collection("Serial")]
public sealed class CliMcpParityTests
{
    // The AgentContextRenderer.ScopeCard body arch_context returns for the quarantined legacy/billing scope —
    // the RenderCommandE2ETests.ScopeBody card without its provenance line (moves with that pin).
    private const string ExpectedScopeCard =
        "## Quarantined scope `legacy/billing`\n\n" +
        "This directory holds the quarantined `legacy/billing` scope: types in `MyApp.Legacy.Billing.*`. " +
        "Here be dragons — do not spread references into it.\n\n" +
        "Dragons: Banker's rounding happens at line-item level, NOT invoice level. " +
        "Nightly reconciliation depends on this. Do not normalize.\n\n" +
        "- `legacy/billing/containment` — Types in `MyApp.Legacy.Billing.*`, except `IBillingFacade` or " +
        "`BillingFacade`, must be referenced only by types in `MyApp.Legacy.Billing.*`, `IBillingFacade` or " +
        "`BillingFacade`. Replacement scheduled (BillingV2, ADR-019); not worth stabilizing.\n" +
        "- Sanctioned surface: `IBillingFacade`, `BillingFacade`.\n" +
        "- Expand: `loadbearing explain legacy/billing/containment`.";

    // The AgentContextRenderer.CautionCard body arch_context returns for the cautioned domain/retry-budget
    // scope — the RenderCommandE2ETests.DomainCautionBody card without its provenance line (moves with that
    // pin). The other scope posture over the same spec, so one harness covers both.
    private const string ExpectedCautionCard =
        "## Cautioned scope `domain/retry-budget`\n\n" +
        "This directory holds the cautioned `domain/retry-budget` scope: types named `RetryPolicy`. " +
        "Here be dragons — the weirdness below is load-bearing; read it before you edit, and do not " +
        "tidy it away.\n\n" +
        "Dragons: RetryPolicy's broad catch is filtered on purpose: the `when` clause is what keeps it green " +
        "under the unfiltered-catch rule, and it is the fixture's one sanctioned broad handler. Keep the " +
        "filter; add cases beside it, never inside it.\n\n" +
        "- `domain/retry-budget/tripwire` — a change set touching this scope is flagged by " +
        "`check --diff-base <ref>`. The retry budget is the one place the domain sanctions a broad catch, " +
        "and every caller relies on the filter.\n" +
        "- Expand: `loadbearing explain domain/retry-budget/tripwire`.";

    // The AgentContextRenderer.LayerCard bodies arch_context returns for the MyApp.Web directory of
    // MyAppLayerSpec — no provenance line (that is a render file-splice concern), mirroring the
    // quarantined-scope card above. Two cards, because two layers are placed there: the Web layer, defined
    // as its project, and the Reporting layer that refines it. Both cover the queried path, and the tool
    // returns whichever cards do.
    private const string ExpectedWebLayerCards =
        "## Layer `Web`\n\n" +
        "This directory holds the `Web` layer. The HTTP surface: controllers and the views they serve. " +
        "Its architecture rules:\n\n" +
        "- `layering/web-not-billing` — The Web layer must not reference types in `MyApp.Legacy.Billing.*`. " +
        "The web layer must reach billing only through the sanctioned facade.\n" +
        "- Expand any rule above with `loadbearing explain <rule-id>`.\n\n" +
        "## Layer `Reporting`\n\n" +
        "This directory holds the `Reporting` layer. Its architecture rules:\n\n" +
        "- `layering/reporting-not-billing` — The Reporting layer must not reference types in " +
        "`MyApp.Legacy.Billing.*`. The reporting slice takes its numbers from the domain, never from the " +
        "legacy biller.\n" +
        RenderedLawText.LeavesBullet +
        RenderedLawText.LeavesNotCircularBullet +
        "- Expand any rule above with `loadbearing explain <rule-id>`.";

    // The truncator's own token → character multiple, restated here because the budget row has to work
    // backwards from a character count to the token budget a client would declare. The two preconditions it
    // asserts on the resulting cap are what keep this honest if the multiple ever moves.
    private const double CharsPerToken = 2.5;

    // The four cold graph documents this suite measures against — one per rung of the grain ladder — each
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

    private static readonly Lazy<Task<CliResult>> ColdGraphIndex =
        new(() => CliRunner.InvokeColdAsync("graph", CliRunner.MyAppSolution, "--json", "--index"));

    // The four cold check documents, memoized for the same reason and against the same spec every row here
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

    private static readonly Lazy<Task<CliResult>> ColdCheckIndex =
        new(() => CliRunner.InvokeColdAsync(
            "check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--json", "--index"));

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

        // A path inside the cautioned scope → that scope's card body. Same tool, same spec, the other scope
        // posture: what an agent reads before editing dragon territory does not depend on whether the
        // dragons are fenced off or merely load-bearing.
        CallToolResult inCaution = await harness.Client.CallToolAsync(
            "arch_context", new Dictionary<string, object?> { ["path"] = "MyApp.Domain/RetryPolicy.cs" }, cancellationToken: Ct);
        inCaution.ShouldHaveTextContent()
            .NormalizedTrimmed()
            .ShouldBe(ExpectedCautionCard);

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
        repo.WriteQuarantineNote();

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

        // A path inside the Web layer directory → the local-rules cards of both layers placed there.
        CallToolResult inLayer = await harness.Client.CallToolAsync(
            "arch_context", new Dictionary<string, object?> { ["path"] = "MyApp.Web/HomeController.cs" }, cancellationToken: Ct);
        inLayer.ShouldHaveTextContent()
            .NormalizedTrimmed()
            .ShouldBe(ExpectedWebLayerCards);

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

        // The floor rung is a caller's flag on both surfaces, not a degrade-only state. It has to be: a rung
        // the server can drop a caller onto but the caller cannot ask for is one no CLI reader can reproduce,
        // and reproducing a degraded answer at its own grain is the property this whole file exists for.
        //
        // arch_graph index ≡ graph --index --json: the project roster, edges as a count.
        CliResult cliGraphIndex = await ColdGraphIndex.Value;
        CallToolResult mcpGraphIndex = await harness.Client.CallToolAsync(
            "arch_graph", new Dictionary<string, object?> { ["index"] = true }, cancellationToken: Ct);
        mcpGraphIndex.ShouldHaveTextContent()
            .NormalizedTrimmed()
            .ShouldBe(cliGraphIndex.Out.NormalizedTrimmed());

        // arch_check index ≡ check --index --json: a verdict per rule ID, the prose gone.
        CliResult cliCheckIndex = await ColdCheckIndex.Value;
        CallToolResult mcpCheckIndex = await harness.Client.CallToolAsync(
            "arch_check", new Dictionary<string, object?> { ["index"] = true }, cancellationToken: Ct);
        mcpCheckIndex.ShouldHaveTextContent()
            .NormalizedTrimmed()
            .ShouldBe(cliCheckIndex.Out.NormalizedTrimmed());
    }

    [Fact]
    public async Task HarnessG_GraphOverTheResponseBudget_ReturnsExactlyTheCliDocumentForTheGrainItLandsOn()
    {
        await ShouldAnswerEachRungWithThatGrainsCliDocumentAsync(
            "arch_graph", [ColdGraph, ColdGraphOverview, ColdGraphSkeleton, ColdGraphIndex],
            result => result.ShouldSucceed());
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
            [ColdCheckFull, ColdCheckOverview, ColdCheckSkeleton, ColdCheckIndex],
            result => result.ShouldReportViolations());
    }

    /// <summary>
    ///     Walks <paramref name="tool" /> down the grain ladder a rung at a time and asserts that each answer
    ///     is byte-identical to the CLI document for the grain it landed on: over a budget between two
    ///     adjacent <paramref name="rungs" /> the tool must return the whole coarser document — nothing cut,
    ///     so the JSON still parses and the client reads a whole answer rather than half of one.
    /// </summary>
    /// <remarks>
    ///     Every budget is derived from the CLI documents either side of the rung under test rather than
    ///     guessed, so this proves the degrade instead of assuming a fixture size — and a rung added below
    ///     the last one is covered by appending it to <paramref name="rungs" />, never by re-picking a
    ///     literal. All the calls share one harness on purpose: the tool re-reads the budget per call, so a
    ///     tighter one takes effect without a second server, and no call names a grain — the degrade is the
    ///     server's own decision.
    /// </remarks>
    /// <param name="tool">The tool to call, finest rung first.</param>
    /// <param name="rungs">The CLI documents, one per rung, ordered finest to coarsest.</param>
    /// <param name="verdict">The exit contract each CLI leg must meet.</param>
    private static async Task ShouldAnswerEachRungWithThatGrainsCliDocumentAsync(
        string tool,
        IReadOnlyList<Lazy<Task<CliResult>>> rungs,
        Action<CliResult> verdict)
    {
        // Arrange
        List<CliResult> documents = [];
        foreach (Lazy<Task<CliResult>> rung in rungs)
        {
            CliResult document = await rung.Value;
            verdict(document);
            documents.Add(document);
        }

        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.For(CliRunner.MyAppSolution, CliRunner.ViolatedSpecDll), Ct);

        // Act & Assert — one pass per step down the ladder. The claim is ladder-wide, not overview-shaped:
        // what comes back is whatever grain the response landed on, spelled exactly as that grain's own flag
        // spells it.
        for (var step = 1; step < documents.Count; step++)
        {
            int tokens = ShouldHaveBudgetBetween(documents[step - 1], documents[step]);
            harness.Environment.SetVariable(
                LoadBearingEnvVars.MaxMcpOutputTokens, tokens.ToString(CultureInfo.InvariantCulture));

            // The plain call, with no grain argument.
            CallToolResult degraded = await harness.Client.CallToolAsync(tool, cancellationToken: Ct);

            string text = degraded.ShouldHaveTextContent();
            text.ShouldNotContain("--- RESPONSE TRUNCATED ---");
            text.NormalizedTrimmed()
                .ShouldBe(documents[step].Out.NormalizedTrimmed());
        }
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
