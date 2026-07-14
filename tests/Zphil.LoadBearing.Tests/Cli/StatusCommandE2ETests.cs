using Shouldly;
using Xunit;

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

        result.Exit.ShouldBe(0);
        result.Out.ShouldContain(
            "FAIL data-access/no-inline-sql (migrate) — 1 grandfathered remaining, 1 new, 0 fixed awaiting acceptance");
        // Freeze containment ratchets like Migrate (uncaptured here) but never suggests promotion.
        result.Out.ShouldContain(
            "FAIL legacy/billing/containment (freeze) — no baseline captured; run 'loadbearing baseline --init' (2 current violations)");
        result.Out.ShouldContain("skip legacy/billing/tripwire (tripwire) — diff-aware; run 'loadbearing check --diff-base <ref>'");
        result.Out.ShouldContain("Burndown: 1 grandfathered remaining, 0 fixed awaiting acceptance.");
    }

    [Fact]
    public async Task Status_ViolatedSpecJson_MatchesGolden()
    {
        CliResult result = await CliRunner.InvokeAsync(
            "status", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--json");

        result.Exit.ShouldBe(0);
        Normalize(result.Out).ShouldBe(Normalize(Golden()));
    }

    private static string Golden()
    {
        return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Cli", "Golden", "violated-status.json"));
    }

    private static string Normalize(string value)
    {
        return value.Replace("\r\n", "\n").Trim();
    }
}