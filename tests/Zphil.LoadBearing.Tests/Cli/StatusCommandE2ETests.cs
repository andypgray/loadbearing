using System.Text.Json;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     End-to-end <c>status</c> against the real MyApp fixture solution. Unlike <c>check</c>, status
///     <em>reports</em> — it exits 0 even though the violated spec has red rules — and prints the Migrate
///     burndown (Invoice grandfathered, Home new), including the per-cell split a ratcheted family rule's
///     row carries. The <c>--json</c> document is pinned by a golden.
/// </summary>
[Collection("Serial")]
public sealed class StatusCommandE2ETests
{
    [Fact]
    public async Task Status_ViolatedSpec_PrintsBurndownAndExitsZero()
    {
        CliResult result = await CliRunner.InvokeAsync("status", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);

        // The Invoice pair covers two DataTable sites, so the burndown states them: the pair count is what
        // the baseline holds and the site count is what the work is. Services-behind-contracts below is one
        // subject declaration, so its line is unchanged — the parenthetical extinguishes itself.
        result.ShouldSucceed(
            "FAIL data-access/no-inline-sql (migrate) — 1 grandfathered remaining (2 sites), 1 new, 0 fixed awaiting acceptance");
        // Quarantine containment ratchets like Migrate (uncaptured here) but never suggests promotion.
        result.Out.ShouldContain(
            "FAIL legacy/billing/containment (quarantine) — no baseline captured; run 'loadbearing baseline --init' (2 current violations)");
        result.Out.ShouldContain("skip legacy/billing/tripwire (tripwire) — diff-aware; run 'loadbearing check --diff-base <ref>'");
        // A caution's tripwire is the same diff-aware skip, byte for byte: the burndown reads a rule's
        // ratchet, and neither tripwire has one, so the posture that produced the row never shows here.
        result.Out.ShouldContain("skip domain/retry-budget/tripwire (tripwire) — diff-aware; run 'loadbearing check --diff-base <ref>'");
        // The row names the role, never the posture: what the reader is told is that no verdict was reached,
        // not which of the two scope postures declined to reach one.
        result.Out.ShouldNotContain("domain/retry-budget/tripwire (caution)");
        result.Out.ShouldContain(
            "FAIL layering/services-behind-contracts (migrate) — 1 grandfathered remaining, 1 new, 0 fixed awaiting acceptance");
        result.Out.ShouldContain("Checked 32 rules: 3 passed, 27 failed, 2 skipped.");
        result.Out.ShouldContain("Burndown: 7 grandfathered remaining (12 sites), 0 fixed awaiting acceptance.");
    }

    [Fact]
    public async Task Status_FamilyRuleWithABaseline_NamesTheCellsItsDebtSitsIn()
    {
        CliResult result = await CliRunner.InvokeAsync("status", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);

        // The row is the row every ratcheted rule prints; the sub-line under it is the family's addition,
        // keyed by the word one cell is and listing only the cells with something left. MyApp.Domain is a
        // cell of the same family and is absent, because nothing reaches into it.
        result.ShouldSucceed(
            "pass layering/projects-reached-only-by-themselves (migrate) — 5 grandfathered remaining (9 sites), 0 new, 0 fixed awaiting acceptance",
            "  projects: MyApp.Legacy.Billing 3 (5 sites), MyApp.Web 2 (4 sites)");
        result.Out.ShouldNotContain("MyApp.Domain 2");
    }

    [Fact]
    public async Task Status_FamilyRuleWithNoBaseline_KeepsItsRowToItself()
    {
        // The negative control on the same solution: layering/projects-independent is a family over the very
        // same partition and reds every one of the pairs its ratcheted sibling grandfathers, and
        // layering/web-cuts-not-circular is a family of layers. One sub-line in the whole report is what says
        // the split reads tolerated debt rather than violations, and reads it off the baseline rather than
        // off the noun.
        CliResult result = await CliRunner.InvokeAsync("status", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);

        result.ShouldSucceed("FAIL layering/projects-independent — 5 violations");
        result.Out.Split('\n')
            .Count(line => line.StartsWith("  ", StringComparison.Ordinal))
            .ShouldBe(1);
    }

    [Fact]
    public async Task Status_FamilyRuleJson_SplitsTheRatchetByCellAndLeavesEveryOtherRuleAlone()
    {
        CliResult result = await CliRunner.InvokeAsync(
            "status", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--json");

        using JsonDocument document = result.ShouldHaveJsonStdout();
        JsonElement[] rules = document.RootElement.GetProperty("rules")
            .EnumerateArray()
            .ToArray();

        // One rule in the document carries the key, and every other ratchet block is the block it was before
        // the key existed — including the two other family rules, which carry no baseline.
        string?[] split = rules
            .Where(CarriesCells)
            .Select(rule => rule.GetProperty("id")
                .GetString())
            .ToArray();
        split.ShouldBe(["layering/projects-reached-only-by-themselves"]);

        JsonElement family = rules.Single(CarriesCells)
            .GetProperty("ratchet");
        JsonElement[] cells = family.GetProperty("cells")
            .EnumerateArray()
            .ToArray();

        // The split is a partition of the row above it, not a second measurement: both totals reconcile.
        cells.Sum(cell => cell.GetProperty("remaining")
                .GetInt32())
            .ShouldBe(family.GetProperty("remaining")
                .GetInt32());
        cells.Sum(cell => cell.GetProperty("remainingSites")
                .GetInt32())
            .ShouldBe(family.GetProperty("remainingSites")
                .GetInt32());
    }

    [Fact]
    public async Task Status_ViolatedSpecJson_MatchesGolden()
    {
        CliResult result = await CliRunner.InvokeAsync(
            "status", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--json");

        result.ShouldSucceed();
        result.Out.ShouldMatchGolden("violated-status.json");
    }

    private static bool CarriesCells(JsonElement rule)
    {
        return rule.TryGetProperty("ratchet", out JsonElement ratchet) && ratchet.TryGetProperty("cells", out _);
    }
}
