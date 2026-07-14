using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     End-to-end <c>baseline</c> against a private, restored copy of the MyApp fixture
///     (<see cref="TempFixtureWorkspace" />, one per fact): <c>--init</c> grandfathers an uncaptured
///     rule's current violations and is idempotent; <c>--accept-reductions</c> shrinks a fixed entry and
///     refuses new growth; and a hand-edited-up baseline (a digest that no longer matches its entries) is
///     refused by <c>check</c> and <c>baseline --init</c> alike. Each fact batches its assertions to keep
///     the expensive CLI/workspace runs to a minimum.
/// </summary>
[Collection("Serial")]
public sealed class BaselineCommandE2ETests
{
    private const string FixedInvoiceController =
        """
        using MyApp.Legacy.Billing;

        namespace MyApp.Web;

        public class InvoiceController
        {
            public decimal Recalculate(decimal amount)
            {
                BillingCalculator calculator = new BillingCalculator();
                return calculator.RoundLineItem(amount, RoundingMode.Bankers);
            }
        }
        """;

    private static readonly string[] ConventionalFile = ["arch", "baselines", "data-access", "no-inline-sql.json"];

    [Fact]
    public async Task BaselineInit_UncapturedRule_CapturesCurrentViolationsAndIsIdempotent()
    {
        using var workspace = new TempFixtureWorkspace();
        string file = workspace.PathOf(ConventionalFile);
        File.Delete(file); // uncaptured rule

        CliResult init = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--init");

        init.Exit.ShouldBe(0);
        init.Out.ShouldContain("wrote");
        // --init grandfathers the current state: both controllers' DataTable sites.
        Normalize(File.ReadAllText(file)).ShouldBe(BothPairsComposed());
        // --init also grandfathers the freeze containment rule into its explicit (uncommitted) baseline —
        // InvoiceController's two interior references into the frozen billing scope. (The freeze --init e2e.)
        Normalize(File.ReadAllText(workspace.PathOf("arch", "violated-freeze-baseline.json")))
            .ShouldBe(ContainmentPairsComposed());

        // check now sees the Migrate rule fully grandfathered.
        CliResult check = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");
        check.Out.ShouldContain("\"grandfathered\": 2");

        // A second --init is a no-op: the rule is already captured, and the bytes do not change.
        byte[] afterFirst = File.ReadAllBytes(file);
        CliResult init2 = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--init");
        init2.Out.ShouldContain("already captured (2 entries)");
        init2.Out.ShouldContain("unchanged");
        File.ReadAllBytes(file).ShouldBe(afterFirst);
    }

    [Fact]
    public async Task BaselineAcceptReductions_ShrinksAndRefusesGrowth()
    {
        using var workspace = new TempFixtureWorkspace();
        // The checked-in conventional file grandfathers Invoice only. "Fix" Invoice (drop its DataTable);
        // Invoice becomes stale, HomeController is still new — so the ratchet shrinks and refuses growth.
        File.WriteAllText(workspace.PathOf("MyApp.Web", "InvoiceController.cs"), FixedInvoiceController);

        CliResult accept = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--accept-reductions");

        accept.Exit.ShouldBe(0);
        accept.Out.ShouldContain("accepted 1 reduction");
        accept.Out.ShouldContain("refused 1 addition");
        // The Invoice entry is gone; the section is empty (with a fresh digest).
        Normalize(File.ReadAllText(workspace.PathOf(ConventionalFile))).ShouldBe(EmptySectionComposed());

        // The ratchet never gated: HomeController is still red, so check still fails.
        CliResult check = await CliRunner.InvokeAsync("check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll);
        check.Exit.ShouldBe(1);
    }

    [Fact]
    public async Task Check_HandEditedUpBaseline_ExitsTwoWithIntegrityError()
    {
        using var workspace = new TempFixtureWorkspace();
        string file = workspace.PathOf(ConventionalFile);
        // Append a HomeController entry by hand without updating the digest — the tamper the ratchet refuses.
        File.WriteAllText(file, File.ReadAllText(file).Replace(
            "        { \"source\": \"T:MyApp.Web.InvoiceController\", \"target\": \"T:System.Data.DataTable\" }\n",
            "        { \"source\": \"T:MyApp.Web.HomeController\", \"target\": \"T:System.Data.DataTable\" },\n" +
            "        { \"source\": \"T:MyApp.Web.InvoiceController\", \"target\": \"T:System.Data.DataTable\" }\n"));

        CliResult check = await CliRunner.InvokeAsync("check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll);
        check.Exit.ShouldBe(2);
        check.Err.ShouldContain("failed its integrity check");

        // status reads the same baselines, so it refuses identically (it never silently passes tamper).
        CliResult status = await CliRunner.InvokeAsync("status", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll);
        status.Exit.ShouldBe(2);
        status.Err.ShouldContain("failed its integrity check");

        // --init cannot distinguish tamper from corruption, so it refuses identically.
        CliResult init = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--init");
        init.Exit.ShouldBe(2);
        init.Err.ShouldContain("failed its integrity check");
    }

    private static string BothPairsComposed()
    {
        return Compose(
            BaselineEntry.ForEdge("T:MyApp.Web.HomeController", "T:System.Data.DataTable"),
            BaselineEntry.ForEdge("T:MyApp.Web.InvoiceController", "T:System.Data.DataTable"));
    }

    private static string EmptySectionComposed()
    {
        return Compose();
    }

    // The freeze containment section captured by --init: InvoiceController's two interior references.
    private static string ContainmentPairsComposed()
    {
        return Compose(
            "legacy/billing/containment",
            BaselineEntry.ForEdge("T:MyApp.Web.InvoiceController", "T:MyApp.Legacy.Billing.BillingCalculator"),
            BaselineEntry.ForEdge("T:MyApp.Web.InvoiceController", "T:MyApp.Legacy.Billing.RoundingMode"));
    }

    private static string Compose(params BaselineEntry[] entries)
    {
        return Compose("data-access/no-inline-sql", entries);
    }

    private static string Compose(string ruleId, params BaselineEntry[] entries)
    {
        return BaselineFormat.ComposeFile(new Dictionary<string, IReadOnlyCollection<BaselineEntry>>(StringComparer.Ordinal)
        {
            [ruleId] = entries
        });
    }

    private static string Normalize(string value)
    {
        return value.Replace("\r\n", "\n");
    }
}