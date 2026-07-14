using Shouldly;
using Xunit;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     End-to-end <c>check</c> runs against the real MyApp fixture solution (each loads a fresh
///     workspace). The violated spec is the acceptance box — one failing rule showing ID, because,
///     fix, and <c>file:line</c> together — plus the freeze containment (uncaptured hard red + facade
///     green + tripwire skip) and the JSON golden pin; the clean spec exits 0.
/// </summary>
[Collection("Serial")]
public sealed class CheckCommandE2ETests
{
    [Fact]
    public async Task Check_ViolatedSpec_ExitsOneWithAllFourComponents()
    {
        CliResult result = await CliRunner.InvokeAsync("check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);

        result.Exit.ShouldBe(1);
        result.Out.ShouldContain("FAIL layering/domain-independent");
        result.Out.ShouldContain("because: Domain is UI-agnostic; transaction boundaries live in services.");
        result.Out.ShouldContain("fix: Define an abstraction in Domain and implement it in Web.");
        result.Out.ShouldContain(
            "MyApp.Domain/OrderService.cs:9 — MyApp.Domain.OrderService references MyApp.Web.HomeController");
    }

    [Fact]
    public async Task Check_ViolatedSpec_RatchetsMigrateRuleWithGrandfatheredInvoiceAndRedHome()
    {
        CliResult result = await CliRunner.InvokeAsync("check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);

        result.Exit.ShouldBe(1);
        // HomeController's DataTable is new code in the old pattern — red.
        result.Out.ShouldContain("FAIL data-access/no-inline-sql");
        result.Out.ShouldContain("MyApp.Web/HomeController.cs:24 — MyApp.Web.HomeController references System.Data.DataTable");
        result.Out.ShouldContain("grandfathered: 1 (baselined; run 'loadbearing status' for burndown)");
        // InvoiceController's DataTable is grandfathered by the conventional baseline — suppressed, not red.
        result.Out.ShouldNotContain("MyApp.Web.InvoiceController references System.Data.DataTable");
    }

    [Fact]
    public async Task Check_ViolatedSpec_FreezeContainmentRedInteriorGreenFacadeTripwireSkips()
    {
        CliResult result = await CliRunner.InvokeAsync("check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);

        result.Exit.ShouldBe(1);
        // Uncaptured containment (its baseline path is deliberately uncommitted) → interior refs are hard red.
        result.Out.ShouldContain("FAIL legacy/billing/containment");
        result.Out.ShouldContain(
            "MyApp.Web/InvoiceController.cs:9 — MyApp.Web.InvoiceController references MyApp.Legacy.Billing.BillingCalculator");
        result.Out.ShouldContain("fix: use `IBillingFacade`");
        result.Out.ShouldContain(
            "hint: no baseline captured for this rule; run 'loadbearing baseline --init' to grandfather existing violations");
        // The facade path (HomeController → IBillingFacade) is the sanctioned surface — green, never listed.
        result.Out.ShouldNotContain("MyApp.Web.HomeController references MyApp.Legacy.Billing.IBillingFacade");
        // The tripwire skips without a --diff-base.
        result.Out.ShouldContain("skip legacy/billing/tripwire");
        result.Out.ShouldContain(
            "skipped: Tripwire: no diff context — run 'loadbearing check --diff-base <ref>' to check changed files against this frozen scope.");
    }

    [Fact]
    public async Task Check_CleanSpec_ExitsZero()
    {
        CliResult result = await CliRunner.InvokeAsync("check", CliRunner.MyAppSolution, "--spec", CliRunner.CleanSpecDll);

        result.Exit.ShouldBe(0);
    }

    [Fact]
    public async Task Check_ViolatedSpecJson_MatchesGolden()
    {
        CliResult result = await CliRunner.InvokeAsync("check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--json");

        result.Exit.ShouldBe(1);
        Normalize(result.Out).ShouldBe(Normalize(Golden()));
    }

    [Fact]
    public async Task Check_MissingSpecFile_ExitsTwoWithMessage()
    {
        CliResult result = await CliRunner.InvokeAsync("check", CliRunner.MyAppSolution, "--spec", "does-not-exist.dll");

        result.Exit.ShouldBe(2);
        result.Err.ShouldContain("was not found");
    }

    private static string Golden()
    {
        return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Cli", "Golden", "violated-check.json"));
    }

    private static string Normalize(string value)
    {
        return value.Replace("\r\n", "\n").Trim();
    }
}