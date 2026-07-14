using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     Freeze tripwire semantics (GRAMMAR §7): a diff-aware touch check over a fabricated
///     <see cref="DiffContext" />. No diff context skips with the pinned reason; a changed file inside
///     the scope warns and the run stays clean (warnings never gate); an outside-scope change is
///     silent; matching is separator- and case-insensitive; multiple touched files order ordinal.
///     Multi-file extraction gives declaration sites assertable paths.
/// </summary>
public sealed class TripwireSemanticsTests
{
    // Alpha and Beta live in the frozen scope; User is outside it and references nothing (so the
    // sibling containment rule stays green and the run's exit signal is tripwire-independent).
    private static readonly CodebaseModel Codebase = CompilationFactory.Extract(
        "App",
        ("App.Legacy/Alpha.cs", "namespace App.Legacy { public class Alpha {} }"),
        ("App.Legacy/Beta.cs", "namespace App.Legacy { public class Beta {} }"),
        ("App.Client/User.cs", "namespace App.Client { public class User {} }"));

    private static void FrozenScope(Arch arch)
    {
        arch.Scope("legacy/frozen")
            .Freeze(arch.Namespace("App.Legacy.*"))
            .Dragons("Alpha and Beta are load-bearing.")
            .Because("Replacement scheduled.");
    }

    private static string ExpectedWarning(string relativePath)
    {
        return $"Changed file '{relativePath}' is inside frozen scope 'legacy/frozen' — does the task actually " +
               "require editing dragon territory? Dragons: loadbearing explain legacy/frozen/tripwire.";
    }

    private static RuleResult Tripwire(DiffContext? diff)
    {
        return Checker.Run(Codebase, BaselineIndex.Empty, diff, FrozenScope).ForRule("legacy/frozen/tripwire");
    }

    [Fact]
    public void NoDiffContext_TripwireSkipsWithPinnedReason()
    {
        RuleResult tripwire = Tripwire(null);

        tripwire.Status.ShouldBe(RuleStatus.Skipped);
        tripwire.SkipReason.ShouldBe(
            "Tripwire: no diff context — run 'loadbearing check --diff-base <ref>' to check changed files against this frozen scope.");
    }

    [Fact]
    public void ChangedFileInsideScope_WarnsAndPassesWithoutGating()
    {
        var diff = new DiffContext("HEAD", "/repo", ["App.Legacy/Alpha.cs"]);
        CheckReport report = Checker.Run(Codebase, BaselineIndex.Empty, diff, FrozenScope);
        RuleResult tripwire = report.ForRule("legacy/frozen/tripwire");

        tripwire.Status.ShouldBe(RuleStatus.Passed);
        CheckWarning warning = tripwire.Warnings.Single();
        warning.Kind.ShouldBe(CheckWarningKind.FrozenScopeTouched);
        warning.Message.ShouldBe(ExpectedWarning("App.Legacy/Alpha.cs"));
        report.HasViolations.ShouldBeFalse();
    }

    [Fact]
    public void ChangedFileOutsideScope_YieldsNoWarnings()
    {
        RuleResult tripwire = Tripwire(new DiffContext("HEAD", "/repo", ["App.Client/User.cs"]));

        tripwire.Status.ShouldBe(RuleStatus.Passed);
        tripwire.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void ChangedFileMatch_IsSeparatorAndCaseInsensitive()
    {
        // A different separator and casing in the diff still matches; the warning uses the codebase path.
        RuleResult tripwire = Tripwire(new DiffContext("HEAD", "/repo", [@"app.legacy\ALPHA.cs"]));

        tripwire.Warnings.Single().Message.ShouldBe(ExpectedWarning("App.Legacy/Alpha.cs"));
    }

    [Fact]
    public void MultipleTouchedFiles_AreOrderedOrdinal()
    {
        // Diff lists Beta before Alpha; the tripwire re-orders ordinal.
        RuleResult tripwire = Tripwire(new DiffContext("HEAD", "/repo", ["App.Legacy/Beta.cs", "App.Legacy/Alpha.cs"]));

        tripwire.Warnings.Select(w => w.Message).ShouldBe(
        [
            ExpectedWarning("App.Legacy/Alpha.cs"),
            ExpectedWarning("App.Legacy/Beta.cs")
        ]);
    }
}