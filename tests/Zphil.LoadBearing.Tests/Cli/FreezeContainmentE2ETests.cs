using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     End-to-end freeze containment against the real MyApp fixture (GRAMMAR §7).
///     The MyAppFrozenSpec resolves its containment to the committed conventional baseline that
///     grandfathers InvoiceController's pre-existing interior references — so a clean run is exit 0 —
///     while a new inbound reference (HomeController → BillingCalculator, added to a private copy) is
///     hard red, with the grandfathered Invoice edges staying green.
/// </summary>
[Collection("Serial")]
public sealed class FreezeContainmentE2ETests
{
    [Fact]
    public async Task Check_FrozenSpec_GrandfatheredInboundPasses_ExitsZero()
    {
        CliResult result = await CliRunner.InvokeAsync("check", CliRunner.MyAppSolution, "--spec", CliRunner.FrozenSpecDll);

        result.Exit.ShouldBe(0);
        result.Out.ShouldContain("pass legacy/billing/containment");
        result.Out.ShouldContain("grandfathered: 2");
        // The facade path (HomeController → IBillingFacade) is the sanctioned surface — never a red edge.
        result.Out.ShouldNotContain("MyApp.Web.HomeController references MyApp.Legacy.Billing.IBillingFacade");
    }

    [Fact]
    public async Task Check_FrozenSpec_NewInboundReference_FailsRed()
    {
        using var workspace = new TempFixtureWorkspace();
        // Add a NEW inbound reference into the frozen scope — not in the grandfather baseline → hard red.
        string homeController = workspace.PathOf("MyApp.Web", "HomeController.cs");
        InsertMember(homeController, "    public BillingCalculator NewCalculator() => new BillingCalculator();");

        CliResult result = await CliRunner.InvokeAsync("check", workspace.SolutionPath, "--spec", CliRunner.FrozenSpecDll);

        result.Exit.ShouldBe(1);
        result.Out.ShouldContain("FAIL legacy/billing/containment");
        result.Out.ShouldContain("MyApp.Web/HomeController.cs:");
        result.Out.ShouldContain("MyApp.Web.HomeController references MyApp.Legacy.Billing.BillingCalculator");
        // The grandfathered InvoiceController edges stay green — not red-listed.
        result.Out.ShouldNotContain("MyApp.Web.InvoiceController references MyApp.Legacy.Billing.BillingCalculator");
    }

    // Inserts a member line just before a class's final closing brace.
    private static void InsertMember(string filePath, string member)
    {
        string original = File.ReadAllText(filePath);
        int lastBrace = original.LastIndexOf('}');
        File.WriteAllText(filePath, original.Substring(0, lastBrace) + member + "\n}\n");
    }
}