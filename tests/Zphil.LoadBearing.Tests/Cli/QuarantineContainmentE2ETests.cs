using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     End-to-end quarantine containment against the real MyApp fixture (GRAMMAR §7).
///     The MyAppQuarantinedSpec resolves its containment to the committed conventional baseline that
///     grandfathers InvoiceController's pre-existing interior references — so a clean run is exit 0 —
///     while a new inbound reference (HomeController → BillingCalculator, added to a private copy) is
///     hard red, with the grandfathered Invoice edges staying green.
/// </summary>
[Collection("Serial")]
public sealed class QuarantineContainmentE2ETests
{
    [Fact]
    public async Task Check_QuarantinedSpec_GrandfatheredInboundPasses_ExitsZero()
    {
        CliResult result = await CliRunner.InvokeAsync("check", CliRunner.MyAppSolution, "--spec", CliRunner.QuarantinedSpecDll);

        result.ShouldSucceed("pass legacy/billing/containment");
        result.Out.ShouldContain("grandfathered: 2");
        // The facade path (HomeController → IBillingFacade) is the sanctioned surface — never a red edge.
        result.Out.ShouldNotContain("MyApp.Web.HomeController references MyApp.Legacy.Billing.IBillingFacade");
    }

    [Fact]
    public async Task Check_QuarantinedSpec_NewInboundReference_FailsRed()
    {
        using var workspace = new TempFixtureWorkspace();
        // Add a NEW inbound reference into the quarantined scope — not in the grandfather baseline → hard red.
        string homeController = workspace.PathOf("MyApp.Web", "HomeController.cs");
        FixtureEdits.AppendMemberLine(homeController, "    public BillingCalculator NewCalculator() => new BillingCalculator();");

        CliResult result = await CliRunner.InvokeAsync("check", workspace.SolutionPath, "--spec", CliRunner.QuarantinedSpecDll);

        result.ShouldReportViolations("FAIL legacy/billing/containment");
        result.Out.ShouldContain("MyApp.Web/HomeController.cs:");
        result.Out.ShouldContain("MyApp.Web.HomeController references MyApp.Legacy.Billing.BillingCalculator");
        // The grandfathered InvoiceController edges stay green — not red-listed.
        result.Out.ShouldNotContain("MyApp.Web.InvoiceController references MyApp.Legacy.Billing.BillingCalculator");
    }
}
