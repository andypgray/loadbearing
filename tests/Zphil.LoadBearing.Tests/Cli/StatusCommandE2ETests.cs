using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     End-to-end <c>status</c> against the real MyApp fixture solution. Unlike <c>check</c>, status
///     <em>reports</em> — it exits 0 even though the violated spec has red rules — and prints the Migrate
///     burndown (Invoice grandfathered, Home new). The <c>--json</c> document is pinned by a golden.
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
        result.Out.ShouldContain("Checked 31 rules: 2 passed, 27 failed, 2 skipped.");
        result.Out.ShouldContain("Burndown: 2 grandfathered remaining (3 sites), 0 fixed awaiting acceptance.");
    }

    [Fact]
    public async Task Status_ViolatedSpecJson_MatchesGolden()
    {
        CliResult result = await CliRunner.InvokeAsync(
            "status", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--json");

        result.ShouldSucceed();
        result.Out.ShouldMatchGolden("violated-status.json");
    }
}
