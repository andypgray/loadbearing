using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     End-to-end acceptance for the derive flow, driven against the golden post-curation
///     artifact (<c>MyAppDerivedSpec</c>) over a private, restored copy of the MyApp fixture
///     (<see cref="TempFixtureWorkspace" />). The one fact walks the acceptance box end to end: a derived
///     proposal <c>check</c>s red on a virgin estate, one <c>baseline --init</c> grandfathers the debt into
///     conventional baselines, the re-check is green, and <c>render</c> emits the agent context. Batched
///     into a single fact because each workspace costs a restore.
/// </summary>
[Collection("Serial")]
public sealed class DeriveFlowE2ETests
{
    /// <summary>
    ///     Mechanizes the acceptance box on the golden derived spec: derive proposal ->
    ///     <c>check</c> runs red with violations (evidence, not failure) -> <c>baseline --init</c> turns
    ///     them into the team's grandfathered baseline -> re-check is green -> <c>render</c> writes the
    ///     managed AGENTS.md context. The checked-in fixture ships two baseline files that would partially
    ///     grandfather the derived rules, so the whole <c>arch/</c> tree is deleted first to simulate a
    ///     virgin estate where the spec meets the code for the first time.
    /// </summary>
    [Fact]
    public async Task GoldenDerivedSpec_VirginEstate_ChecksRedInitsBaselinesThenChecksGreenAndRenders()
    {
        using var workspace = new TempFixtureWorkspace();
        // Virgin estate: drop the fixture's committed baselines so the derived rules meet the code raw.
        string archDir = workspace.PathOf("arch");
        if (Directory.Exists(archDir)) Directory.Delete(archDir, true);

        // Check the derived proposal: red, with the survey's debt surfacing as evidence. 4 rules fail
        // (2 layering-migrate edges, 2 inline-SQL edges, 1 handler-naming shape, 2 containment edges = 7
        // violations across the three Migrate rules and the Freeze containment); the 2 Enforce directions
        // pass; the Freeze tripwire skips with no --diff-base.
        CliResult checkRed = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.DerivedSpecDll, "--json");

        checkRed.Exit.ShouldBe(1);
        checkRed.Out.ShouldContain("\"rulesFailed\": 4");
        checkRed.Out.ShouldContain("\"rulesPassed\": 2");
        checkRed.Out.ShouldContain("\"rulesSkipped\": 1");
        checkRed.Out.ShouldContain("\"violations\": 7");

        // The human baselines the remainder: --init grandfathers every current violation into the
        // conventional default path per failing rule (one per Migrate rule + the Freeze containment).
        CliResult init = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.DerivedSpecDll, "--init");

        init.Exit.ShouldBe(0);
        File.Exists(workspace.PathOf("arch", "baselines", "layering", "domain-independent.json")).ShouldBeTrue();
        File.Exists(workspace.PathOf("arch", "baselines", "data-access", "no-inline-sql.json")).ShouldBeTrue();
        File.Exists(workspace.PathOf("arch", "baselines", "naming", "handlers.json")).ShouldBeTrue();
        File.Exists(workspace.PathOf("arch", "baselines", "legacy", "billing", "containment.json")).ShouldBeTrue();

        // Re-check: the debt is now the signed baseline, so the run is green with zero active violations.
        CliResult checkGreen = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.DerivedSpecDll, "--json");

        checkGreen.Exit.ShouldBe(0);
        checkGreen.Out.ShouldContain("\"rulesFailed\": 0");
        checkGreen.Out.ShouldContain("\"violations\": 0");

        // Render the agent context: the root managed block and the frozen-scope card for legacy/billing.
        CliResult render = await CliRunner.InvokeAsync(
            "render", workspace.SolutionPath, "--spec", CliRunner.DerivedSpecDll);

        render.Exit.ShouldBe(0);
        string rootAgents = workspace.PathOf("AGENTS.md");
        string scopeAgents = workspace.PathOf("MyApp.Legacy.Billing", "AGENTS.md");
        File.Exists(rootAgents).ShouldBeTrue();
        File.ReadAllText(rootAgents).ShouldContain("<!-- loadbearing:begin -->");
        File.Exists(scopeAgents).ShouldBeTrue();
        File.ReadAllText(scopeAgents).ShouldContain("## Frozen scope `legacy/billing`");
    }
}