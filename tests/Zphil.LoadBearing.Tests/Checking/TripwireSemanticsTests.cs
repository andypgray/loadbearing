using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     Scope tripwire semantics (GRAMMAR §7): a diff-aware touch check over a fabricated
///     <see cref="DiffContext" />. No diff context skips with the pinned reason; a changed file inside
///     the scope warns and the run stays clean (warnings never gate); an outside-scope change is
///     silent; matching is separator- and case-insensitive; multiple touched files order ordinal.
///     Multi-file extraction gives declaration sites assertable paths.
/// </summary>
/// <remarks>
///     The path-matching rows are the quarantine's alone: both postures share one tripwire builder and one
///     changed-file walk, so a caution twin of each would pin the same code twice. What the caution rows
///     pin is the half that differs — the skip reason, the warning kind, and the message's voice.
/// </remarks>
public sealed class TripwireSemanticsTests
{
    // Match case sensitivity follows the OS file system (the shared PathComparison rule).
    private static readonly bool CaseInsensitiveFileSystem = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    // Alpha and Beta live in the quarantined scope; User is outside it and references nothing (so the
    // sibling containment rule stays green and the run's exit signal is tripwire-independent).
    private static readonly CodebaseModel Codebase = CompilationFactory.Extract(
        "App",
        ("App.Legacy/Alpha.cs", "namespace App.Legacy { public class Alpha {} }"),
        ("App.Legacy/Beta.cs", "namespace App.Legacy { public class Beta {} }"),
        ("App.Client/User.cs", "namespace App.Client { public class User {} }"));

    private static void QuarantinedScope(Arch arch)
    {
        arch.Scope("legacy/quarantined")
            .Quarantine(arch.Namespace("App.Legacy.*"))
            .Dragons("Alpha and Beta are load-bearing.")
            .Because("Replacement scheduled.");
    }

    private static string ExpectedWarning(string relativePath)
    {
        return $"Changed file '{relativePath}' is inside quarantined scope 'legacy/quarantined' — does the task actually " +
               "require editing dragon territory? Dragons: loadbearing explain legacy/quarantined/tripwire.";
    }

    private static RuleResult Tripwire(DiffContext? diff)
    {
        return Checker.Run(Codebase, BaselineIndex.Empty, diff, QuarantinedScope)
            .ForRule("legacy/quarantined/tripwire");
    }

    // The same region under the other scope posture, so the caution rows differ from the quarantine rows in
    // exactly one thing: which verb declared the scope.
    private static void CautionedScope(Arch arch)
    {
        arch.Scope("legacy/cautioned")
            .Caution(arch.Namespace("App.Legacy.*"))
            .Dragons("Alpha and Beta are load-bearing.")
            .Because("Nothing replaces them; the weirdness is the interface.");
    }

    private static string ExpectedCautionWarning(string relativePath)
    {
        return $"Changed file '{relativePath}' is inside cautioned scope 'legacy/cautioned' — read the dragons " +
               "before editing: loadbearing explain legacy/cautioned/tripwire.";
    }

    private static RuleResult CautionTripwire(DiffContext? diff)
    {
        return Checker.Run(Codebase, BaselineIndex.Empty, diff, CautionedScope)
            .ForRule("legacy/cautioned/tripwire");
    }

    [Fact]
    public void NoDiffContext_TripwireSkipsWithPinnedReason()
    {
        RuleResult tripwire = Tripwire(null);

        tripwire.Status.ShouldBe(RuleStatus.Skipped);
        tripwire.SkipReason.ShouldBe(
            "Tripwire: no diff context — run 'loadbearing check --diff-base <ref>' to check changed files against this quarantined scope.");
    }

    [Fact]
    public void ChangedFileInsideScope_WarnsAndPassesWithoutGating()
    {
        var diff = new DiffContext("/repo", ["App.Legacy/Alpha.cs"]);
        CheckReport report = Checker.Run(Codebase, BaselineIndex.Empty, diff, QuarantinedScope);
        RuleResult tripwire = report.ForRule("legacy/quarantined/tripwire");

        // The same path the message names, carried structurally: a renderer that needs a location — SARIF
        // does — must not have to read one back out of the prose.
        tripwire.ShouldHaveWarnedOnce(
            CheckWarningKind.QuarantinedScopeTouched, ExpectedWarning("App.Legacy/Alpha.cs"),
            "App.Legacy/Alpha.cs");
        report.HasViolations.ShouldBeFalse();
    }

    [Fact]
    public void ChangedFileOutsideScope_YieldsNoWarnings()
    {
        RuleResult tripwire = Tripwire(new DiffContext("/repo", ["App.Client/User.cs"]));

        tripwire.ShouldHavePassedClean();
    }

    [Fact]
    public void ChangedFileMatch_IsSeparatorInsensitive()
    {
        // A backslash separator in the diff still matches the forward-slash codebase path on every OS;
        // the warning uses the codebase path.
        RuleResult tripwire = Tripwire(new DiffContext("/repo", [@"App.Legacy\Alpha.cs"]));

        tripwire.Warnings.Single()
            .Message.ShouldBe(ExpectedWarning("App.Legacy/Alpha.cs"));
    }

    [Fact]
    public void ChangedFileMatch_OnCaseInsensitiveFileSystem_IgnoresCase()
    {
        // A differently-cased diff path matches only where the OS file system is case-insensitive.
        Assert.SkipUnless(CaseInsensitiveFileSystem, "Case-insensitive path matching is Windows/macOS behavior.");
        RuleResult tripwire = Tripwire(new DiffContext("/repo", [@"app.legacy\ALPHA.cs"]));

        tripwire.Warnings.Single()
            .Message.ShouldBe(ExpectedWarning("App.Legacy/Alpha.cs"));
    }

    [Fact]
    public void ChangedFileMatch_OnCaseSensitiveFileSystem_DoesNotMatchCaseVariant()
    {
        // On Linux a case-variant path is a different file, so the tripwire must not fire on it.
        Assert.SkipWhen(CaseInsensitiveFileSystem, "Case-sensitive path matching is Linux behavior.");
        RuleResult tripwire = Tripwire(new DiffContext("/repo", [@"app.legacy\ALPHA.cs"]));

        tripwire.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void NoDiffContext_CautionTripwireSkipsWithItsOwnReason()
    {
        CautionTripwire(null)
            .ShouldHaveSkipped(
                "Tripwire: no diff context — run 'loadbearing check --diff-base <ref>' to check changed files against this cautioned scope.");
    }

    [Fact]
    public void ChangedFileInsideCautionedScope_WarnsInItsOwnVoiceAndPasses()
    {
        var diff = new DiffContext("/repo", ["App.Legacy/Alpha.cs"]);
        CheckReport report = Checker.Run(Codebase, BaselineIndex.Empty, diff, CautionedScope);
        RuleResult tripwire = report.ForRule("legacy/cautioned/tripwire");

        tripwire.ShouldHaveWarnedOnce(
            CheckWarningKind.CautionedScopeTouched, ExpectedCautionWarning("App.Legacy/Alpha.cs"),
            "App.Legacy/Alpha.cs");
        // A caution has no second rule, so this is the whole run: the posture with no red state cannot
        // produce one however loudly its tripwire fires.
        report.HasViolations.ShouldBeFalse();
    }

    [Fact]
    public void ChangedFileOutsideCautionedScope_YieldsNoWarnings()
    {
        RuleResult tripwire = CautionTripwire(new DiffContext("/repo", ["App.Client/User.cs"]));

        tripwire.ShouldHavePassedClean();
    }

    [Fact]
    public void TripwireWarnings_ReachTheWireAsCamelCasedKindNames()
    {
        // The kind is an enum on the model and a string on `check --json`, cased by the one shared
        // converter — so a kind added to the enum reaches the wire with no renderer edit at all. That is
        // convenient and entirely unproven until something reads the document back, which is this row.
        var diff = new DiffContext("/repo", ["App.Legacy/Alpha.cs"]);

        Checker.Run(Codebase, BaselineIndex.Empty, diff, QuarantinedScope)
            .JsonReport()
            .ShouldContain("\"quarantinedScopeTouched\"");
        Checker.Run(Codebase, BaselineIndex.Empty, diff, CautionedScope)
            .JsonReport()
            .ShouldContain("\"cautionedScopeTouched\"");
    }

    [Fact]
    public void MultipleTouchedFiles_AreOrderedOrdinal()
    {
        // Diff lists Beta before Alpha; the tripwire re-orders ordinal.
        RuleResult tripwire = Tripwire(new DiffContext("/repo", ["App.Legacy/Beta.cs", "App.Legacy/Alpha.cs"]));

        tripwire.Warnings.Select(w => w.Message)
            .ShouldBe(
            [
                ExpectedWarning("App.Legacy/Alpha.cs"),
                ExpectedWarning("App.Legacy/Beta.cs")
            ]);
    }
}
