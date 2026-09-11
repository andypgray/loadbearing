using Shouldly;
using Xunit;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Hosting;
using Zphil.LoadBearing.Tests.Checking;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     Pins every <c>status</c> line shape (<see cref="StatusFormatter" />) over synthetic
///     <see cref="RuleResult" />s — no workspace: Enforce pass/FAIL with counts, the four Migrate states
///     (captured-failing, promotable, interim-awaiting-acceptance, and uncaptured), the Quarantine
///     containment ratchet lines (which never promote) and both postures' tripwire diff-aware skip, the
///     narrowing skip that overrides posture dispatch entirely, the three measure terms and their
///     self-extinguishing behaviour, plus the burndown summary and the nudge it carries once per run.
/// </summary>
public sealed class StatusFormatterTests
{
    // One rule of each posture so the formatter can be pinned over real ArchRules.
    private static readonly ArchitectureModel Model = Checker.Model(arch =>
    {
        arch.Rule("layering/billing-independent")
            .Enforce(arch.Types.MustHaveSuffix("X"))
            .Because("b");
        arch.Rule("layering/domain-independent")
            .Enforce(arch.Types.MustHaveSuffix("Y"))
            .Because("b");
        arch.Rule("data-access/no-inline-sql")
            .Migrate("old", arch.Types.MustHaveSuffix("Z"))
            .Because("b");
        arch.Scope("legacy/billing")
            .Quarantine(arch.Namespace("App.Legacy.*"))
            .Dragons("d")
            .Because("b");
        arch.Scope("shared/utilities")
            .Caution(arch.Namespace("App.Shared.*"))
            .Dragons("d")
            .Because("b");
    });

    private static string Line(RuleResult result)
    {
        return StatusFormatter.Lines(new CheckReport([result]))
            .First();
    }

    [Fact]
    public void Enforce_Pass_HasNoDetail()
    {
        Line(Result(Model.Rule("layering/billing-independent"), RuleStatus.Passed))
            .ShouldBe("pass layering/billing-independent");
    }

    [Fact]
    public void Enforce_Fail_ShowsViolationCount()
    {
        Line(Result(Model.Rule("layering/domain-independent"), RuleStatus.Failed, 2))
            .ShouldBe("FAIL layering/domain-independent — 2 violations");
    }

    [Fact]
    public void Enforce_PassWithWarning_ShowsWarningCount()
    {
        Line(Result(Model.Rule("layering/billing-independent"), RuleStatus.Passed, warnings: 1))
            .ShouldBe("pass layering/billing-independent — 1 warning");
    }

    [Fact]
    public void QuarantineContainment_Uncaptured_SuggestsInit()
    {
        Line(Result(Model.Rule("legacy/billing/containment"), RuleStatus.Failed, 2))
            .ShouldBe(
                "FAIL legacy/billing/containment (quarantine) — no baseline captured; run 'loadbearing baseline --init' (2 current violations)");
    }

    [Fact]
    public void QuarantineContainment_Grandfathered_ShowsBurndownAndNeverPromotes()
    {
        string line = Line(Result(Model.Rule("legacy/billing/containment"), RuleStatus.Failed, 1, grandfathered: 2, captured: true));

        line.ShouldBe("FAIL legacy/billing/containment (quarantine) — 2 grandfathered remaining, 1 new, 0 fixed awaiting acceptance");
        line.ShouldNotContain("promotable");
    }

    [Fact]
    public void QuarantineContainment_BurnedToZero_ReadsPlainNotPromotable()
    {
        // Quarantine→Migrate is a human decision; a burned-to-zero containment never suggests promotion.
        string line = Line(Result(Model.Rule("legacy/billing/containment"), RuleStatus.Passed, captured: true));

        line.ShouldBe("pass legacy/billing/containment (quarantine) — 0 grandfathered remaining");
        line.ShouldNotContain("promotable");
    }

    [Fact]
    public void QuarantineTripwire_ReadsDiffAwareSkip()
    {
        Line(Result(Model.Rule("legacy/billing/tripwire"), RuleStatus.Skipped, skipReason: "whatever"))
            .ShouldBe("skip legacy/billing/tripwire (tripwire) — diff-aware; run 'loadbearing check --diff-base <ref>'");
    }

    [Fact]
    public void CautionTripwire_ReadsDiffAwareSkip()
    {
        // The same line the quarantine's tripwire prints, and it has to be: what a reader is being told is
        // that the run had no diff to check, which is the same fact under either posture. Without the arm
        // the dispatch falls through to the Enforce line and a rule the run never ran reads "pass".
        Line(Result(Model.Rule("shared/utilities/tripwire"), RuleStatus.Skipped, skipReason: "whatever"))
            .ShouldBe("skip shared/utilities/tripwire (tripwire) — diff-aware; run 'loadbearing check --diff-base <ref>'");
    }

    [Fact]
    public void Migrate_NarrowingSkipped_ReadsAsASkipRatherThanAPassWithABurndown()
    {
        // Posture dispatch is what makes this dangerous: without the skip branch a narrowing-skipped Migrate
        // rule falls into the ratchet line and reads "pass … 0 new, 2 fixed awaiting acceptance" — a pass
        // and a burndown for a rule the run never measured.
        string line = Line(Result(
            Model.Rule("data-access/no-inline-sql"), RuleStatus.Skipped, stale: 2, captured: true,
            skipReason: "'BillingOnly.slnf' narrowed this run."));

        line.ShouldBe("skip data-access/no-inline-sql — 'BillingOnly.slnf' narrowed this run.");
        line.ShouldNotContain("awaiting acceptance");
    }

    [Fact]
    public void Enforce_NarrowingSkipped_ReadsAsASkipCarryingItsReason()
    {
        Line(Result(
                Model.Rule("layering/billing-independent"), RuleStatus.Skipped,
                skipReason: "'BillingOnly.slnf' narrowed this run."))
            .ShouldBe("skip layering/billing-independent — 'BillingOnly.slnf' narrowed this run.");
    }

    [Fact]
    public void Migrate_CapturedFailing_ShowsRemainingNewAndAwaiting()
    {
        Line(Result(Model.Rule("data-access/no-inline-sql"), RuleStatus.Failed, 1, grandfathered: 1, captured: true))
            .ShouldBe("FAIL data-access/no-inline-sql (migrate) — 1 grandfathered remaining, 1 new, 0 fixed awaiting acceptance");
    }

    [Theory]
    [InlineData(1, "1 grandfathered remaining")]
    [InlineData(3, "1 grandfathered remaining (3 sites)")]
    public void Migrate_GrandfatheredPairCoveringSeveralSites_StatesTheSiteTotal(int sitesEach, string expected)
    {
        // The burndown's real unit: one pair over three sites is three migrations, not one. The parenthetical
        // is self-extinguishing — at one site per pair it says nothing the pair count does not, and the line
        // is byte-for-byte the one this rule printed before the measure existed.
        Line(Result(
                Model.Rule("data-access/no-inline-sql"), RuleStatus.Failed, 1, grandfathered: 1, captured: true,
                sitesEach: sitesEach))
            .ShouldBe($"FAIL data-access/no-inline-sql (migrate) — {expected}, 1 new, 0 fixed awaiting acceptance");
    }

    [Fact]
    public void Migrate_ShrunkEntries_TrailTheAwaitingAcceptanceTerm()
    {
        // A matched entry whose pair came in under the count it records: a real reduction, never red, and
        // reported so 'baseline --accept-reductions' has something to lower the count to. It trails the stale
        // term rather than joining the head of the list, because like it, it names a state a write clears.
        Line(Result(
                Model.Rule("data-access/no-inline-sql"), RuleStatus.Passed, grandfathered: 4, captured: true,
                shrunk: 1))
            .ShouldBe(
                "pass data-access/no-inline-sql (migrate) — 4 grandfathered remaining, 0 new, "
                + "0 fixed awaiting acceptance, 1 shrunk");
    }

    [Fact]
    public void Summary_UncountedEntries_CarryTheRecordingNudgeOnceUnderTheBurndown()
    {
        // The nudge is advice, so it rides the summary's uncounted clause and nowhere else: repeated down
        // every rule line it would be noise on a run whose remedy is one command. The rule line still says
        // how many of its own entries are uncounted, which is the number the command will record.
        var report = new CheckReport(
        [
            Result(
                Model.Rule("data-access/no-inline-sql"), RuleStatus.Passed, grandfathered: 2, captured: true,
                sitesEach: 4, uncounted: 2)
        ]);

        IReadOnlyList<string> lines = StatusFormatter.Lines(report);

        lines[0]
            .ShouldBe(
                "pass data-access/no-inline-sql (migrate) — 2 grandfathered remaining (8 sites), 0 new, "
                + "0 fixed awaiting acceptance, 2 uncounted");
        lines.Last()
            .ShouldBe(
                "Checked 1 rules: 1 passed, 0 failed, 0 skipped. Burndown: 2 grandfathered remaining (8 sites), "
                + "0 fixed awaiting acceptance, 2 uncounted; run 'loadbearing baseline --accept-reductions' "
                + "to record site counts.");
    }

    [Fact]
    public void Migrate_PromotableWhenBaselineEmpty()
    {
        Line(Result(Model.Rule("data-access/no-inline-sql"), RuleStatus.Passed, captured: true))
            .ShouldBe("pass data-access/no-inline-sql (migrate) — 0 remaining; promotable to Enforce (baseline is empty)");
    }

    [Fact]
    public void Migrate_InterimSuggestsAcceptReductions()
    {
        Line(Result(Model.Rule("data-access/no-inline-sql"), RuleStatus.Passed, stale: 2, captured: true))
            .ShouldBe(
                "pass data-access/no-inline-sql (migrate) — 0 remaining, 2 fixed awaiting acceptance; " +
                "run 'loadbearing baseline --accept-reductions'");
    }

    [Fact]
    public void Migrate_Uncaptured_SuggestsInit()
    {
        Line(Result(Model.Rule("data-access/no-inline-sql"), RuleStatus.Failed, 2))
            .ShouldBe("FAIL data-access/no-inline-sql (migrate) — no baseline captured; run 'loadbearing baseline --init' (2 current violations)");
    }

    [Fact]
    public void Summary_ReportsRuleCountsAndBurndown()
    {
        var report = new CheckReport(
        [
            Result(Model.Rule("layering/billing-independent"), RuleStatus.Passed),
            Result(Model.Rule("data-access/no-inline-sql"), RuleStatus.Passed, grandfathered: 1, captured: true),
            Result(Model.Rule("layering/domain-independent"), RuleStatus.Failed, 1),
            Result(Model.Rule("layering/domain-independent"), RuleStatus.Failed, 1),
            Result(Model.Rule("layering/domain-independent"), RuleStatus.Failed, 1)
        ]);

        StatusFormatter.Lines(report)
            .Last()
            .ShouldBe(
                "Checked 5 rules: 2 passed, 3 failed, 0 skipped. Burndown: 1 grandfathered remaining, 0 fixed awaiting acceptance.");
    }

    private static RuleResult Result(
        ArchRule rule, RuleStatus status, int violations = 0, int warnings = 0, int grandfathered = 0, int stale = 0,
        bool captured = false, string? skipReason = null, int sitesEach = 0, int shrunk = 0, int uncounted = 0)
    {
        IReadOnlyList<Violation> remaining = sitesEach > 0
            ? SitedDummies(grandfathered, sitesEach)
            : Dummies(grandfathered);

        return new RuleResult(
            rule,
            status,
            Dummies(violations),
            Enumerable.Range(0, warnings)
                .Select(_ => new CheckWarning(CheckWarningKind.InertTarget, "w"))
                .ToList(),
            skipReason,
            remaining,
            new RatchetMeasure(stale, shrunk, uncounted),
            captured);
    }

    /// <summary>
    ///     Stand-in violations carrying no site at all, which is what makes them the right default: the site
    ///     total prints only when it exceeds the pair count, so a rule built from these prints the line it
    ///     printed before the measure existed and every pre-measure row keeps its pin unchanged.
    /// </summary>
    private static IReadOnlyList<Violation> Dummies(int count)
    {
        return Enumerable.Range(0, count)
            .Select(_ => Violation.RuleError("x"))
            .ToList();
    }

    /// <summary>
    ///     The site-carrying sibling: <paramref name="count" /> stand-ins of <paramref name="sites" /> distinct
    ///     <c>file:line</c> sites apiece — the shape a real grandfathered pair has, and the only one that
    ///     reaches the site total.
    /// </summary>
    private static IReadOnlyList<Violation> SitedDummies(int count, int sites)
    {
        return Enumerable.Range(0, count)
            .Select(index => Violation.Shape(
                SyntheticNodes.Type($"App.Subject{index}"), SyntheticNodes.Sites($"Subject{index}.cs", sites)))
            .ToList();
    }
}
