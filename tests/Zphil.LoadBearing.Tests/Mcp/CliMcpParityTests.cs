using ModelContextProtocol.Protocol;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Mcp;
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
///     <see cref="Cli.Mcp.Infrastructure.IdleTimeoutWatchdog" /> in-flight counter.
/// </summary>
[Collection("Serial")]
public sealed class CliMcpParityTests
{
    // The AgentContextRenderer.ScopeCard body arch_context returns for the frozen legacy/billing scope —
    // the RenderCommandE2ETests.ScopeBody card without its provenance line (moves with that pin).
    private const string ExpectedScopeCard =
        "## Frozen scope `legacy/billing`\n\n" +
        "This directory holds the frozen `legacy/billing` scope. Here be dragons — do not spread references into it.\n\n" +
        "Dragons: Banker's rounding happens at line-item level, NOT invoice level. " +
        "Nightly reconciliation depends on this. Do not normalize.\n\n" +
        "- `legacy/billing/containment` — Types in `MyApp.Legacy.Billing.*`, except `IBillingFacade` or " +
        "`BillingFacade` must be referenced only by types in `MyApp.Legacy.Billing.*`, `IBillingFacade` or " +
        "`BillingFacade`. Replacement scheduled (BillingV2, ADR-019); not worth stabilizing.\n" +
        "- Sanctioned surface: `IBillingFacade`, `BillingFacade`.\n" +
        "- Expand: `loadbearing explain legacy/billing/containment`.";

    // The AgentContextRenderer.LayerCard body arch_context returns for the Web layer of MyAppLayerSpec —
    // no provenance line (that is a render file-splice concern), mirroring the frozen-scope card above.
    private const string ExpectedWebLayerCard =
        "## Layer `Web`\n\n" +
        "This directory holds the `Web` layer. Its architecture rules:\n\n" +
        "- `layering/web-not-billing` — The Web layer must not reference types in `MyApp.Legacy.Billing.*`. " +
        "The web layer must reach billing only through the sanctioned facade.\n" +
        "- Expand any rule above with `loadbearing explain <rule-id>`.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task HarnessA_ViolatedSpec_CheckStatusExplain_MatchCli()
    {
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            Binding(CliRunner.MyAppSolution, CliRunner.ViolatedSpecDll), Ct);

        // arch_check ≡ check --json (CLI exits 1 on the violation; the tool never reports IsError). The
        // ViolatedSpec carries every violation kind including the member-subject rule naming/async-suffix
        // (memberShape / subjectMember, GRAMMAR §4.6), so this byte-parity covers member subjects too.
        CliResult cliCheck = await CliRunner.InvokeAsync(
            "check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--json");
        cliCheck.Exit.ShouldBe(1);
        CallToolResult mcpCheck = await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct);
        mcpCheck.IsError.ShouldNotBe(true);
        Normalize(TextOf(mcpCheck)).ShouldBe(Normalize(cliCheck.Out));

        // arch_status ≡ status --json.
        CliResult cliStatus = await CliRunner.InvokeAsync(
            "status", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--json");
        CallToolResult mcpStatus = await harness.Client.CallToolAsync("arch_status", cancellationToken: Ct);
        Normalize(TextOf(mcpStatus)).ShouldBe(Normalize(cliStatus.Out));

        // arch_graph ≡ graph --json (spec-independent; the survey ignores the bound spec, and graph takes no --spec).
        CliResult cliGraph = await CliRunner.InvokeAsync("graph", CliRunner.MyAppSolution, "--json");
        CallToolResult mcpGraph = await harness.Client.CallToolAsync("arch_graph", cancellationToken: Ct);
        Normalize(TextOf(mcpGraph)).ShouldBe(Normalize(cliGraph.Out));

        // arch_explain <known> ≡ explain <known> stdout.
        CliResult cliExplain = await CliRunner.InvokeAsync(
            "explain", "layering/domain-independent", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);
        CallToolResult mcpExplain = await harness.Client.CallToolAsync(
            "arch_explain", new Dictionary<string, object?> { ["ruleId"] = "layering/domain-independent" }, cancellationToken: Ct);
        Normalize(TextOf(mcpExplain)).ShouldBe(Normalize(cliExplain.Out));

        // arch_explain <unknown> IsError text ≡ explain <unknown> stderr (CLI exit 2).
        CliResult cliUnknown = await CliRunner.InvokeAsync(
            "explain", "unknown/id", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);
        cliUnknown.Exit.ShouldBe(2);
        CallToolResult mcpUnknown = await harness.Client.CallToolAsync(
            "arch_explain", new Dictionary<string, object?> { ["ruleId"] = "unknown/id" }, cancellationToken: Ct);
        mcpUnknown.IsError.ShouldBe(true);
        Normalize(TextOf(mcpUnknown)).ShouldBe(Normalize(cliUnknown.Err));
    }

    [Fact]
    public async Task HarnessB_CleanSpec_Check_MatchesCli()
    {
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            Binding(CliRunner.MyAppSolution, CliRunner.CleanSpecDll), Ct);

        CliResult cliCheck = await CliRunner.InvokeAsync(
            "check", CliRunner.MyAppSolution, "--spec", CliRunner.CleanSpecDll, "--json");
        cliCheck.Exit.ShouldBe(0);
        CallToolResult mcpCheck = await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct);

        Normalize(TextOf(mcpCheck)).ShouldBe(Normalize(cliCheck.Out));
    }

    [Fact]
    public async Task HarnessC_RenderSpec_Context_InScopeCardAndOutOfScopePointer()
    {
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            Binding(CliRunner.MyAppSolution, CliRunner.RenderSpecDll), Ct);

        // A path inside the frozen scope → that scope's card body.
        CallToolResult inScope = await harness.Client.CallToolAsync(
            "arch_context", new Dictionary<string, object?> { ["path"] = "MyApp.Legacy.Billing/BillingCalculator.cs" }, cancellationToken: Ct);
        Normalize(TextOf(inScope)).ShouldBe(ExpectedScopeCard);

        // A path no scope covers → the pinned pointer line (echoing the query path). The RenderSpec's
        // Domain/Web layers carry no anchored rules, so no layer card competes here.
        CallToolResult outScope = await harness.Client.CallToolAsync(
            "arch_context", new Dictionary<string, object?> { ["path"] = "MyApp.Web/HomeController.cs" }, cancellationToken: Ct);
        Normalize(TextOf(outScope)).ShouldBe(
            "No architecture scope covers 'MyApp.Web/HomeController.cs'. Architecture context for this solution lives in " +
            "the root AGENTS.md managed block; expand any rule with 'loadbearing explain <rule-id>'.");
    }

    [Fact]
    public async Task HarnessD_FrozenSpec_CheckDiffBase_MatchesCliAndWarnsTripwire()
    {
        using var repo = new TempGitRepo();
        // A brand-new untracked file in dragon territory — the tripwire's agent-hook case.
        File.WriteAllText(
            repo.PathOf("MyApp.Legacy.Billing", "LegacyNote.cs"),
            "namespace MyApp.Legacy.Billing;\n\npublic class LegacyNote;\n");

        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            Binding(repo.SolutionPath, CliRunner.FrozenSpecDll), Ct);

        CliResult cliCheck = await CliRunner.InvokeAsync(
            "check", repo.SolutionPath, "--spec", CliRunner.FrozenSpecDll, "--json", "--diff-base", "HEAD");
        CallToolResult mcpCheck = await harness.Client.CallToolAsync(
            "arch_check", new Dictionary<string, object?> { ["diffBase"] = "HEAD" }, cancellationToken: Ct);

        string mcpText = Normalize(TextOf(mcpCheck));
        mcpText.ShouldBe(Normalize(cliCheck.Out));
        mcpText.ShouldContain("frozenScopeTouched");
    }

    [Fact]
    public async Task HarnessE_LayerSpec_Context_InLayerCardAndOutOfScopePointer()
    {
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            Binding(CliRunner.MyAppSolution, CliRunner.LayerSpecDll), Ct);

        // A path inside the Web layer directory → that layer's local-rules card.
        CallToolResult inLayer = await harness.Client.CallToolAsync(
            "arch_context", new Dictionary<string, object?> { ["path"] = "MyApp.Web/HomeController.cs" }, cancellationToken: Ct);
        Normalize(TextOf(inLayer)).ShouldBe(ExpectedWebLayerCard);

        // A path no layer or frozen scope covers → the reworded pointer line (echoing the query path).
        CallToolResult outScope = await harness.Client.CallToolAsync(
            "arch_context", new Dictionary<string, object?> { ["path"] = "MyApp.Domain/Order.cs" }, cancellationToken: Ct);
        Normalize(TextOf(outScope)).ShouldBe(
            "No architecture scope covers 'MyApp.Domain/Order.cs'. Architecture context for this solution lives in " +
            "the root AGENTS.md managed block; expand any rule with 'loadbearing explain <rule-id>'.");
    }

    private static McpServerBinding Binding(string? solution, string? spec)
    {
        string workingDirectory = solution is null
            ? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(Path.GetFullPath(solution))!;
        return new McpServerBinding(solution, spec, workingDirectory);
    }

    private static string TextOf(CallToolResult result)
    {
        return ((TextContentBlock)result.Content.Single()).Text;
    }

    private static string Normalize(string value)
    {
        return value.Replace("\r\n", "\n").Trim();
    }
}