using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     End-to-end Freeze tripwire against a real git repo (<see cref="TempGitRepo" />, Phase 6
///     acceptance). <c>check --diff-base HEAD</c> warns for changed files inside the frozen scope and
///     never gates on those warnings: an untracked new file in dragon territory (the agent-hook case,
///     found via <c>git ls-files --others</c>) warns and exits 0, while a tracked change combined with a
///     new interior reference exits 1 — the exit code is containment-driven, the tripwire only warns.
/// </summary>
[Collection("Serial")]
public sealed class TripwireDiffE2ETests
{
    [Fact]
    public async Task CheckDiffBase_UntrackedFileInFrozenScope_WarnsAndExitsZero()
    {
        using var repo = new TempGitRepo();
        // A brand-new, still-untracked file in the frozen billing project — SDK globs compile it in.
        File.WriteAllText(
            repo.PathOf("MyApp.Legacy.Billing", "LegacyNote.cs"),
            "namespace MyApp.Legacy.Billing;\n\npublic class LegacyNote;\n");

        CliResult result = await CliRunner.InvokeAsync(
            "check", repo.SolutionPath, "--spec", CliRunner.FrozenSpecDll, "--diff-base", "HEAD");

        result.Exit.ShouldBe(0);
        result.Out.ShouldContain("warn legacy/billing/tripwire");
        result.Out.ShouldContain(
            "warning: Changed file 'MyApp.Legacy.Billing/LegacyNote.cs' is inside frozen scope 'legacy/billing' — " +
            "does the task actually require editing dragon territory? Dragons: loadbearing explain legacy/billing/tripwire.");
    }

    [Fact]
    public async Task CheckDiffBase_ContainmentRedPlusTouch_ExitsOneWithBoth()
    {
        using var repo = new TempGitRepo();
        // Touch a tracked file inside the frozen scope (tripwire warning) ...
        File.AppendAllText(repo.PathOf("MyApp.Legacy.Billing", "BillingCalculator.cs"), "\n// touched by the tripwire test\n");
        // ... and add a NEW interior reference from outside the scope (containment red).
        InsertMember(repo.PathOf("MyApp.Web", "HomeController.cs"), "    public BillingCalculator NewCalculator() => new BillingCalculator();");

        CliResult result = await CliRunner.InvokeAsync(
            "check", repo.SolutionPath, "--spec", CliRunner.FrozenSpecDll, "--diff-base", "HEAD");

        // Exit code is containment-driven only; the tripwire warning rides alongside.
        result.Exit.ShouldBe(1);
        result.Out.ShouldContain("FAIL legacy/billing/containment");
        result.Out.ShouldContain("MyApp.Web.HomeController references MyApp.Legacy.Billing.BillingCalculator");
        result.Out.ShouldContain("warn legacy/billing/tripwire");
        result.Out.ShouldContain("Changed file 'MyApp.Legacy.Billing/BillingCalculator.cs' is inside frozen scope 'legacy/billing'");
    }

    // Inserts a member line just before a class's final closing brace.
    private static void InsertMember(string filePath, string member)
    {
        string original = File.ReadAllText(filePath);
        int lastBrace = original.LastIndexOf('}');
        File.WriteAllText(filePath, original.Substring(0, lastBrace) + member + "\n}\n");
    }
}