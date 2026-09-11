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
    private const string InlineSqlRule = "data-access/no-inline-sql";

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

        init.ShouldSucceed("wrote");
        // --init grandfathers the current state: both controllers' DataTable sites.
        File.ReadAllText(file)
            .NormalizedLines()
            .ShouldBe(BothPairsComposed());
        // --init also grandfathers the quarantine containment rule into its explicit (uncommitted) baseline —
        // InvoiceController's two interior references into the quarantined billing scope. (The quarantine --init e2e.)
        File.ReadAllText(workspace.PathOf("arch", "violated-quarantine-baseline.json"))
            .NormalizedLines()
            .ShouldBe(ContainmentPairsComposed());

        // The survey ends by naming every failing Enforce rule, after the per-file lines, so a first --init
        // explains why check will stay red.
        init.Out.ShouldContain("failing with no baseline to capture");
        init.Out.ShouldContain("layering/domain-independent");
        init.Out.ShouldContain("naming/nonexistent");
        init.Out.ShouldContain("exceptions/domain-throws-domain");
        init.Out.ShouldContain("async/accept-cancellation");
        init.Out.ShouldContain("exceptions/no-unfiltered-catch");
        init.Out.ShouldContain("exceptions/no-swallowed-catch");
        init.Out.ShouldContain("exceptions/no-bare-bcl-throw");
        init.Out.IndexOf("wrote", StringComparison.Ordinal)
            .ShouldBeLessThan(init.Out.IndexOf("failing with no baseline to capture", StringComparison.Ordinal));

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
        File.ReadAllBytes(file)
            .ShouldBe(afterFirst);
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

        accept.ShouldSucceed("accepted 1 reduction");
        accept.Out.ShouldContain("refused 1 addition");
        // The Invoice entry is gone; the section is empty (with a fresh digest).
        File.ReadAllText(workspace.PathOf(ConventionalFile))
            .NormalizedLines()
            .ShouldBe(EmptySectionComposed());

        // The ratchet never gated: HomeController is still red, so check still fails.
        CliResult check = await CliRunner.InvokeAsync("check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll);
        check.ShouldReportViolations();
    }

    [Fact]
    public async Task Check_HandEditedUpBaseline_ExitsTwoWithIntegrityError()
    {
        using var workspace = new TempFixtureWorkspace();
        string file = workspace.PathOf(ConventionalFile);
        // Append a HomeController entry by hand without updating the digest — the tamper the ratchet refuses.
        // Forged in the shape of the entry beside it, site count and all, so what the refusal rests on is the
        // digest rather than anything the walk could notice about the line.
        File.WriteAllText(file, File.ReadAllText(file)
            .Replace(
                "        { \"source\": \"T:MyApp.Web.InvoiceController\", \"target\": \"T:System.Data.DataTable\", \"siteCount\": 2 }\n",
                "        { \"source\": \"T:MyApp.Web.HomeController\", \"target\": \"T:System.Data.DataTable\", \"siteCount\": 2 },\n" +
                "        { \"source\": \"T:MyApp.Web.InvoiceController\", \"target\": \"T:System.Data.DataTable\", \"siteCount\": 2 }\n"));

        CliResult check = await CliRunner.InvokeAsync("check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll);
        check.ShouldRefuseWith("failed its integrity check");

        // status reads the same baselines, so it refuses identically (it never silently passes tamper).
        CliResult status = await CliRunner.InvokeAsync("status", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll);
        status.ShouldRefuseWith("failed its integrity check");

        // --init cannot distinguish tamper from corruption, so it refuses identically.
        CliResult init = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--init");
        init.ShouldRefuseWith("failed its integrity check");
    }

    // Both controllers declare the DataTable twice — as a return type and as a construction, on two lines —
    // so --init records two sites under each pair.
    private static string BothPairsComposed()
    {
        return BaselineComposer.Compose(
            InlineSqlRule,
            BaselineEntry.ForEdge("T:MyApp.Web.HomeController", "T:System.Data.DataTable")
                .WithSiteCount(2),
            BaselineEntry.ForEdge("T:MyApp.Web.InvoiceController", "T:System.Data.DataTable")
                .WithSiteCount(2));
    }

    private static string EmptySectionComposed()
    {
        return BaselineComposer.Compose(InlineSqlRule);
    }

    // The quarantine containment section captured by --init: InvoiceController's two interior references,
    // over three sites between them (the calculator is declared and then constructed; the mode is read once).
    private static string ContainmentPairsComposed()
    {
        return BaselineComposer.Compose(
            "legacy/billing/containment",
            BaselineEntry.ForEdge("T:MyApp.Web.InvoiceController", "T:MyApp.Legacy.Billing.BillingCalculator")
                .WithSiteCount(2),
            BaselineEntry.ForEdge("T:MyApp.Web.InvoiceController", "T:MyApp.Legacy.Billing.RoundingMode")
                .WithSiteCount(1));
    }
}
