using System.Text.Json;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     The migrate posture's two acceptances, against one private copy of the fixture whose
///     <c>DataTable</c> pairs are fully grandfathered (the clean spec's <c>arch/clean-baseline.json</c>):
///     a baseline entry keys on stable symbol IDs, so it survives a file move; and it records how many
///     sites it grandfathers, so a second helping of the old pattern inside an already-grandfathered pair
///     is new code and red. One class, so the fixture copy and its restore are paid once.
/// </summary>
/// <remarks>
///     A copy rather than the committed fixture, and that is the point rather than mere hygiene: the four
///     <c>check</c> goldens and the MCP truncation-budget pins are all measured against the fixture as
///     committed, so growing it in place would move every one of them for a fact none of them is about.
/// </remarks>
[Collection("Serial")]
public sealed class MigrateE2ETests
{
    // A third inline DataTable, written where the surrounding code already does it — the exact shape the
    // measure exists to catch, and the one a pair-grained ratchet passed.
    private const string ThirdSite = "\n    public System.Data.DataTable ExportDrafts() => new System.Data.DataTable();\n";

    [Fact]
    public async Task Check_AfterFileMove_GrandfatheredEntriesStillMatch()
    {
        using var workspace = new TempFixtureWorkspace();

        // Plain move within the project: the SDK glob still compiles it, and `namespace MyApp.Web;`
        // (declared in the file, not folder-derived) is unchanged — so T:MyApp.Web.InvoiceController holds.
        string source = workspace.PathOf("MyApp.Web", "InvoiceController.cs");
        string destination = workspace.PathOf("MyApp.Web", "Controllers", "InvoiceController.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(source, destination);

        // The clean spec grandfathers both DataTable sites via arch/clean-baseline.json. The DocID
        // survived the move, so the Migrate rule stays fully grandfathered and the whole spec is clean.
        CliResult check = await CliRunner.InvokeAsync("check", workspace.SolutionPath, "--spec", CliRunner.CleanSpecDll);
        check.ShouldSucceed();

        // Belt-and-braces: the burndown confirms both remain grandfathered and none went stale.
        CliResult status = await CliRunner.InvokeAsync(
            "status", workspace.SolutionPath, "--spec", CliRunner.CleanSpecDll, "--json");
        JsonElement ratchet = CheckJson.Rule(status.Out, "data-access/no-inline-sql")
            .GetProperty("ratchet");
        ratchet.GetProperty("remaining")
            .GetInt32()
            .ShouldBe(2);
        ratchet.GetProperty("stale")
            .GetInt32()
            .ShouldBe(0);
    }

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
        // pair nobody ever baselined — read off this rule's own violation, since the count alone would match
        // any rule's block.
        CheckJson.Violations(machine.Out, "data-access/no-inline-sql")
            .ShouldHaveSingleItem()
            .GetProperty("grandfatheredSiteCount")
            .GetInt32()
            .ShouldBe(2);

        // Code scanning already has an alert for this pair from the run that baselined it, so the state is
        // `updated` rather than `new`, and the entry's suppression no longer rides with it.
        string sarif = await File.ReadAllTextAsync(sarifPath, Ct);
        sarif.SarifResults()
            .Select(result => result.GetProperty("baselineState")
                .GetString())
            .ShouldContain("updated");
    }
}
