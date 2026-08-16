using Shouldly;
using Xunit;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Tests.Checking;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     Pins every <c>status</c> line shape (<see cref="StatusFormatter" />) over synthetic
///     <see cref="RuleResult" />s — no workspace: Enforce pass/FAIL with counts, the four Migrate states
///     (captured-failing, promotable, interim-awaiting-acceptance, and uncaptured), the Quarantine
///     containment ratchet lines (which never promote) and the tripwire's diff-aware skip, the
///     narrowing skip that overrides posture dispatch entirely, plus the burndown summary.
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
    });

    private static ArchRule Rule(string id)
    {
        return Model.Rules.Single(r => r.Id == id);
    }

    private static string Line(RuleResult result)
    {
        return StatusFormatter.Lines(new CheckReport([result]))
            .First();
    }

    [Fact]
    public void Enforce_Pass_HasNoDetail()
    {
        Line(Result(Rule("layering/billing-independent"), RuleStatus.Passed))
            .ShouldBe("pass layering/billing-independent");
    }

    [Fact]
    public void Enforce_Fail_ShowsViolationCount()
    {
        Line(Result(Rule("layering/domain-independent"), RuleStatus.Failed, 2))
            .ShouldBe("FAIL layering/domain-independent — 2 violations");
    }

    [Fact]
    public void Enforce_PassWithWarning_ShowsWarningCount()
    {
        Line(Result(Rule("layering/billing-independent"), RuleStatus.Passed, warnings: 1))
            .ShouldBe("pass layering/billing-independent — 1 warning");
    }

    [Fact]
    public void QuarantineContainment_Uncaptured_SuggestsInit()
    {
        Line(Result(Rule("legacy/billing/containment"), RuleStatus.Failed, 2))
            .ShouldBe(
                "FAIL legacy/billing/containment (quarantine) — no baseline captured; run 'loadbearing baseline --init' (2 current violations)");
    }

    [Fact]
    public void QuarantineContainment_Grandfathered_ShowsBurndownAndNeverPromotes()
    {
        string line = Line(Result(Rule("legacy/billing/containment"), RuleStatus.Failed, 1, grandfathered: 2, captured: true));

        line.ShouldBe("FAIL legacy/billing/containment (quarantine) — 2 grandfathered remaining, 1 new, 0 fixed awaiting acceptance");
        line.ShouldNotContain("promotable");
    }

    [Fact]
    public void QuarantineContainment_BurnedToZero_ReadsPlainNotPromotable()
    {
        // Quarantine→Migrate is a human decision; a burned-to-zero containment never suggests promotion.
        string line = Line(Result(Rule("legacy/billing/containment"), RuleStatus.Passed, captured: true));

        line.ShouldBe("pass legacy/billing/containment (quarantine) — 0 grandfathered remaining");
        line.ShouldNotContain("promotable");
    }

    [Fact]
    public void QuarantineTripwire_ReadsDiffAwareSkip()
    {
        Line(Result(Rule("legacy/billing/tripwire"), RuleStatus.Skipped, skipReason: "whatever"))
            .ShouldBe("skip legacy/billing/tripwire (tripwire) — diff-aware; run 'loadbearing check --diff-base <ref>'");
    }

    [Fact]
    public void Migrate_NarrowingSkipped_ReadsAsASkipRatherThanAPassWithABurndown()
    {
        // Posture dispatch is what makes this dangerous: without the skip branch a narrowing-skipped Migrate
        // rule falls into the ratchet line and reads "pass … 0 new, 2 fixed awaiting acceptance" — a pass
        // and a burndown for a rule the run never measured.
        string line = Line(Result(
            Rule("data-access/no-inline-sql"), RuleStatus.Skipped, stale: 2, captured: true,
            skipReason: "'BillingOnly.slnf' narrowed this run."));

        line.ShouldBe("skip data-access/no-inline-sql — 'BillingOnly.slnf' narrowed this run.");
        line.ShouldNotContain("awaiting acceptance");
    }

    [Fact]
    public void Enforce_NarrowingSkipped_ReadsAsASkipCarryingItsReason()
    {
        Line(Result(
                Rule("layering/billing-independent"), RuleStatus.Skipped,
                skipReason: "'BillingOnly.slnf' narrowed this run."))
            .ShouldBe("skip layering/billing-independent — 'BillingOnly.slnf' narrowed this run.");
    }

    [Fact]
    public void Migrate_CapturedFailing_ShowsRemainingNewAndAwaiting()
    {
        Line(Result(Rule("data-access/no-inline-sql"), RuleStatus.Failed, 1, grandfathered: 1, captured: true))
            .ShouldBe("FAIL data-access/no-inline-sql (migrate) — 1 grandfathered remaining, 1 new, 0 fixed awaiting acceptance");
    }

    [Fact]
    public void Migrate_PromotableWhenBaselineEmpty()
    {
        Line(Result(Rule("data-access/no-inline-sql"), RuleStatus.Passed, captured: true))
            .ShouldBe("pass data-access/no-inline-sql (migrate) — 0 remaining; promotable to Enforce (baseline is empty)");
    }

    [Fact]
    public void Migrate_InterimSuggestsAcceptReductions()
    {
        Line(Result(Rule("data-access/no-inline-sql"), RuleStatus.Passed, stale: 2, captured: true))
            .ShouldBe(
                "pass data-access/no-inline-sql (migrate) — 0 remaining, 2 fixed awaiting acceptance; " +
                "run 'loadbearing baseline --accept-reductions'");
    }

    [Fact]
    public void Migrate_Uncaptured_SuggestsInit()
    {
        Line(Result(Rule("data-access/no-inline-sql"), RuleStatus.Failed, 2))
            .ShouldBe("FAIL data-access/no-inline-sql (migrate) — no baseline captured; run 'loadbearing baseline --init' (2 current violations)");
    }

    [Fact]
    public void Summary_ReportsRuleCountsAndBurndown()
    {
        var report = new CheckReport(
        [
            Result(Rule("layering/billing-independent"), RuleStatus.Passed),
            Result(Rule("data-access/no-inline-sql"), RuleStatus.Passed, grandfathered: 1, captured: true),
            Result(Rule("layering/domain-independent"), RuleStatus.Failed, 1),
            Result(Rule("layering/domain-independent"), RuleStatus.Failed, 1),
            Result(Rule("layering/domain-independent"), RuleStatus.Failed, 1)
        ]);

        StatusFormatter.Lines(report)
            .Last()
            .ShouldBe(
                "Checked 5 rules: 2 passed, 3 failed, 0 skipped. Burndown: 1 grandfathered remaining, 0 fixed awaiting acceptance.");
    }

    private static RuleResult Result(
        ArchRule rule, RuleStatus status, int violations = 0, int warnings = 0, int grandfathered = 0, int stale = 0,
        bool captured = false, string? skipReason = null)
    {
        return new RuleResult(
            rule,
            status,
            Dummies(violations),
            Enumerable.Range(0, warnings)
                .Select(_ => new CheckWarning(CheckWarningKind.InertTarget, "w"))
                .ToList(),
            skipReason,
            Dummies(grandfathered),
            stale,
            captured);
    }

    private static IReadOnlyList<Violation> Dummies(int count)
    {
        return Enumerable.Range(0, count)
            .Select(_ => Violation.RuleError("x"))
            .ToList();
    }
}
