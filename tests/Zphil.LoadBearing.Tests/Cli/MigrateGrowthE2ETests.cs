using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     The growth acceptance: a baseline entry records how many sites it grandfathers, so a second helping
///     of the old pattern inside an already-grandfathered pair is new code and red. Against a private copy
///     of the fixture whose two <c>DataTable</c> pairs are fully grandfathered at two sites apiece (the
///     clean spec's <c>arch/clean-baseline.json</c>), adding a third <c>DataTable</c> use to
///     <c>InvoiceController</c> takes that pair over its recorded count: <c>check</c> exits 1 listing all
///     three sites and naming the allowance, while the untouched <c>HomeController</c> pair stays
///     grandfathered.
/// </summary>
/// <remarks>
///     A copy rather than the committed fixture, and that is the point rather than mere hygiene: the four
///     <c>check</c> goldens and the MCP truncation-budget pins are all measured against the fixture as
///     committed, so growing it in place would move every one of them for a fact none of them is about.
/// </remarks>
[Collection("Serial")]
public sealed class MigrateGrowthE2ETests
{
    // A third inline DataTable, written where the surrounding code already does it — the exact shape the
    // measure exists to catch, and the one a pair-grained ratchet passed.
    private const string ThirdSite = "\n    public System.Data.DataTable ExportDrafts() => new System.Data.DataTable();\n";

    [Fact]
    public async Task Check_ExtraSiteInsideGrandfatheredPair_ExitsOneWithTheGrownTrailer()
    {
        using var workspace = new TempFixtureWorkspace();

        // The fixture's InvoiceController -> DataTable pair sits at :14 and :16 and its entry records two
        // sites. Appending a method inside the class puts a third at :19, under the same symbol ID, so
        // nothing about the pair's identity moves — only its measure.
        string controller = workspace.PathOf("MyApp.Web", "InvoiceController.cs");
        string original = await File.ReadAllTextAsync(controller, Ct);
        int classClose = original.LastIndexOf('}');
        await File.WriteAllTextAsync(
            controller, original[..classClose] + ThirdSite + original[classClose..], Ct);

        CliResult check = await CliRunner.InvokeAsync("check", workspace.SolutionPath, "--spec", CliRunner.CleanSpecDll);

        // Every site of the grown pair is red, not only the new one: which site is new is a diff question,
        // and the count is deliberately lossy — that lossiness is what buys immunity to line churn.
        check.ShouldReportViolations(
            "MyApp.Web/InvoiceController.cs:14 — MyApp.Web.InvoiceController references System.Data.DataTable",
            "MyApp.Web/InvoiceController.cs:16 — MyApp.Web.InvoiceController references System.Data.DataTable",
            "MyApp.Web/InvoiceController.cs:19 — MyApp.Web.InvoiceController references System.Data.DataTable",
            "  grown: 1 grandfathered pair exceeded the baseline site count (2 baselined, 3 observed)",
            // The untouched pair is still within its count, so the rule is red on one pair and blessing the
            // other in the same block.
            "  grandfathered: 1 (baselined; run 'loadbearing status' for burndown)");

        using TempDirectory temp = TestTempRoot.Fresh("migrate-growth");
        string sarifPath = temp.PathOf("grown.sarif");
        CliResult machine = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.CleanSpecDll, "--json", "--sarif", sarifPath);

        machine.ShouldReportViolations();
        machine.Out.ShouldHaveViolationAtSites(
            "data-access/no-inline-sql", ("source", "MyApp.Web.InvoiceController"), 3);
        // The allowance beside the measurement, which is what tells a reader the red is growth rather than a
        // pair nobody ever baselined.
        machine.Out.ShouldContain("\"grandfatheredSiteCount\": 2");

        // Code scanning already has an alert for this pair from the run that baselined it, so the state is
        // `updated` rather than `new`, and the entry's suppression no longer rides with it.
        string sarif = await File.ReadAllTextAsync(sarifPath, Ct);
        sarif.ShouldContain("\"baselineState\": \"updated\"");
    }
}
