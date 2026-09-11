using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     End-to-end <c>baseline</c> against a private, restored copy of the MyApp fixture
///     (<see cref="TempFixtureWorkspace" />, one per fact): <c>--init</c> grandfathers an uncaptured
///     rule's current violations and is idempotent; <c>--accept-reductions</c> shrinks a fixed entry and
///     refuses new growth; a hand-edited-up baseline (a forged entry whose seal does not match it) is
///     refused by <c>check</c> and <c>baseline --init</c> alike; and a file whose shape its declared
///     version does not describe is refused by <c>check</c> and <c>status</c> without either ever
///     reporting the rule as uncaptured. Each fact batches its assertions to keep the expensive
///     CLI/workspace runs to a minimum.
/// </summary>
[Collection("Serial")]
public sealed class BaselineCommandE2ETests
{
    private const string InlineSqlRule = "data-access/no-inline-sql";

    // The opening words of the hint a rule with nothing captured carries, in both the check report and
    // the status line. Named here because the rows below assert its absence, and an absence pinned
    // against a re-typed fragment stops meaning anything the moment the real sentence is reworded.
    private const string UncapturedHint = "no baseline captured";

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
        // The Invoice entry is gone, and with it its seal; the section renders as an empty array.
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
        // Append a HomeController entry by hand — the tamper the ratchet refuses. Forged in the shape of the
        // entry beside it, site count and all, down to a seal that is sixteen lowercase hex characters and
        // simply not this entry's, so what the refusal rests on is the seal rather than anything the walk
        // could notice about the line. HomeController sorts first, so the forgery takes the comma and the
        // captured entry stays last.
        const string forged =
            "        { \"source\": \"T:MyApp.Web.HomeController\", \"target\": \"T:System.Data.DataTable\", " +
            "\"siteCount\": 2, \"seal\": \"0123456789abcdef\" },\n";
        File.WriteAllText(file, File.ReadAllText(file)
            .Replace("      \"entries\": [\n", "      \"entries\": [\n" + forged));

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

    [Fact]
    public async Task Check_BaselineInAShapeItsVersionDoesNotDescribe_RefusesRatherThanReportingItUncaptured()
    {
        using var workspace = new TempFixtureWorkspace();
        string file = workspace.PathOf(ConventionalFile);
        // A whole-file digest beside the entries: the envelope every baseline carried before integrity
        // moved onto the entry, under a version that now describes a sealed file. The digest's own value
        // is never reached — the key is a stranger under this version before anything recomputes it — so
        // sixty-four zeros stand in for a real one, and what the row is about is which of two diagnoses
        // a reader gives a file it cannot read.
        var envelope = $"  \"schemaVersion\": {BaselineFormat.SchemaVersion},\n";
        var digest = $"  \"digest\": \"{new string('0', 64)}\",\n";
        File.WriteAllText(file, File.ReadAllText(file).Replace(envelope, envelope + digest));

        CliResult check = await CliRunner.InvokeAsync("check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll);
        check.ShouldRefuseWith("is not valid", "unknown property 'digest'", "no-inline-sql.json");

        // The point of the row, and why the absence is asserted rather than left to the exit code: a file
        // the reader refuses and a rule nothing has captured are different diagnoses whose recoveries run
        // opposite ways. Uncaptured advertises 'baseline --init', which grandfathers every current
        // violation fresh and drops the attribution the ratchet exists to keep — the worst available move
        // against a file whose entries are all still there and merely unreadable. So a refusal must never
        // reach the operator wearing the other one's hint, on either channel.
        check.Out.ShouldNotContain(UncapturedHint);
        check.Err.ShouldNotContain(UncapturedHint);

        // status reads the same files through the same store, so it refuses on the same terms.
        CliResult status = await CliRunner.InvokeAsync("status", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll);
        status.ShouldRefuseWith("is not valid", "unknown property 'digest'", "no-inline-sql.json");
        status.Out.ShouldNotContain(UncapturedHint);
        status.Err.ShouldNotContain(UncapturedHint);
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
