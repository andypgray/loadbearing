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
///     Both rotted shapes are here too — a target that matched nothing and a subject that did — reading
///     <c>not measured</c> on each of the two arms they reach, carrying no acceptance nudge, never
///     promotable, and adding the signal's own cure on a sub-line under an unchanged row.
/// </summary>
public sealed class StatusFormatterTests
{
    // One rule of each posture so the formatter can be pinned over real ArchRules. The three family rules
    // carry the two cell words and both baseline states, which is what decides whether a row gains a
    // sub-line and what that sub-line is keyed by.
    private static readonly ArchitectureModel Model = Checker.Model(arch =>
    {
        Layer checking = arch.Layer("Checking", "App.Checking.*");
        Layer rendering = arch.Layer("Rendering", "App.Rendering.*");

        arch.Rule("layering/cuts-not-circular")
            .Migrate("old", arch.Each(checking, rendering).MustNotHaveCircularReferences())
            .Because("b");
        arch.Rule("layering/cuts-independent")
            .Enforce(arch.Each(checking, rendering).MustNotReferenceEachOther())
            .Because("b");
        arch.Rule("modules/independent")
            .Migrate("old", arch.Each(arch.Projects.Matching("App.*")).MustNotReferenceEachOther())
            .Because("b");
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
                "Checked 1 rule: 1 passed, 0 failed, 0 skipped. Burndown: 2 grandfathered remaining (8 sites), "
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
    public void FamilyMigrate_DebtInSeveralCells_NamesEachOfThemUnderAnUnchangedRow()
    {
        // The burndown's next question after "how much": one cell holding eleven of the twelve pairs is a
        // different morning's work from twelve spread evenly, and the row alone cannot say which this is.
        IReadOnlyList<Violation> debt = [.. SitedDummies(1, 3, "Checking"), .. SitedDummies(11, 8, "Rendering")];

        IReadOnlyList<string> lines = StatusFormatter.Lines(
            new CheckReport([Result(Model.Rule("layering/cuts-not-circular"), RuleStatus.Passed, captured: true, debt: debt)]));

        // The row is byte-for-byte the row this rule printed before the split existed — the sub-line rides
        // under it, never inside it, which is what keeps the quoted-count gate's anchored pattern matching.
        lines[0]
            .ShouldBe(
                "pass layering/cuts-not-circular (migrate) — 12 grandfathered remaining (91 sites), 0 new, "
                + "0 fixed awaiting acceptance");
        lines[1]
            .ShouldBe("  layers: Checking 1 (3 sites), Rendering 11 (88 sites)");
    }

    [Fact]
    public void FamilyMigrate_DebtInOneCell_KeepsTheKeySingular()
    {
        // The same inflection the summary tails carry: the key counts the cells shown, not the family's.
        IReadOnlyList<string> lines = StatusFormatter.Lines(
            new CheckReport(
            [
                Result(
                    Model.Rule("layering/cuts-not-circular"), RuleStatus.Passed, captured: true,
                    debt: SitedDummies(3, 3, "Rendering"))
            ]));

        lines[1]
            .ShouldBe("  layer: Rendering 3 (9 sites)");
    }

    [Fact]
    public void FamilyMigrate_OverProjects_KeysOnTheProjectWordAndExtinguishesTheSiteClausePerCell()
    {
        // The site parenthetical is the row's own, reused per cell: a cell whose pairs are one site apiece
        // says nothing the pair count does not, and says it beside a cell that does.
        IReadOnlyList<Violation> debt = [.. SitedDummies(2, 2, "App.Api"), .. SitedDummies(1, 1, "App.Web")];

        IReadOnlyList<string> lines = StatusFormatter.Lines(
            new CheckReport([Result(Model.Rule("modules/independent"), RuleStatus.Passed, captured: true, debt: debt)]));

        lines[1]
            .ShouldBe("  projects: App.Api 2 (4 sites), App.Web 1");
    }

    [Fact]
    public void FamilyMigrate_NothingRemaining_KeepsItsRowToItself()
    {
        // A burned-down family has nowhere to point, and an empty key under the promotion suggestion would
        // be one more line saying nothing.
        IReadOnlyList<string> lines = StatusFormatter.Lines(
            new CheckReport([Result(Model.Rule("layering/cuts-not-circular"), RuleStatus.Passed, captured: true)]));

        lines[0]
            .ShouldBe("pass layering/cuts-not-circular (migrate) — 0 remaining; promotable to Enforce (baseline is empty)");
        lines.Count.ShouldBe(2, "the rule's row and the summary, and nothing between them");
    }

    [Fact]
    public void FamilyEnforce_HasNoSubLineHoweverMuchItReds()
    {
        // Keyed on the baseline like every other reader of "is this rule ratcheted": a family with no
        // baseline has no debt to place, only violations to fix, and check is where those are read.
        IReadOnlyList<string> lines = StatusFormatter.Lines(
            new CheckReport([Result(Model.Rule("layering/cuts-independent"), RuleStatus.Failed, 4)]));

        lines[0]
            .ShouldBe("FAIL layering/cuts-independent — 4 violations");
        lines.Count.ShouldBe(2);
    }

    [Fact]
    public void NonFamilyMigrate_WithDebt_HasNoSubLine()
    {
        // The other half of the same gate: a plain subject has no cells, so its debt has nowhere to be
        // grouped and its row reads exactly as it always has.
        IReadOnlyList<string> lines = StatusFormatter.Lines(
            new CheckReport(
            [
                Result(
                    Model.Rule("data-access/no-inline-sql"), RuleStatus.Passed, grandfathered: 2, captured: true,
                    sitesEach: 3)
            ]));

        lines[0]
            .ShouldBe(
                "pass data-access/no-inline-sql (migrate) — 2 grandfathered remaining (6 sites), 0 new, "
                + "0 fixed awaiting acceptance");
        lines.Count.ShouldBe(2);
    }

    [Fact]
    public void Migrate_RottedTarget_ReadsNotMeasuredAndWithholdsTheAcceptReductionsNudge()
    {
        // The harm this whole shape exists to prevent. A forbidden target that matched nothing warns and
        // passes with no violations, so every captured entry goes unmatched — which read as "fixed awaiting
        // acceptance" under a nudge to run the one command that then deleted the section outright.
        IReadOnlyList<string> lines = StatusFormatter.Lines(
            new CheckReport(
            [
                Result(
                    Model.Rule("data-access/no-inline-sql"), RuleStatus.Passed, stale: 12, captured: true,
                    inertHint: "THE-TARGET-CURE")
            ]));

        lines[0]
            .ShouldBe("pass data-access/no-inline-sql (migrate) — 0 remaining, 12 not measured");
        lines[0]
            .ShouldNotContain("accept-reductions");
    }

    [Fact]
    public void Migrate_RottedSubject_ReadsNotMeasuredOnTheThreeCountArmToo()
    {
        // The other rotted shape reaches the other arm: an empty subject IS one new violation, so the rule
        // fails and the row carries all three counters. The term has to be honest in both places, or half the
        // rot keeps reading as paid-down debt.
        IReadOnlyList<string> lines = StatusFormatter.Lines(
            new CheckReport(
            [
                Result(
                    Model.Rule("data-access/no-inline-sql"), RuleStatus.Failed, captured: true, stale: 12,
                    emptySubjectHint: "THE-SUBJECT-CURE")
            ]));

        lines[0]
            .ShouldBe(
                "FAIL data-access/no-inline-sql (migrate) — 0 grandfathered remaining, 1 new, 12 not measured");
    }

    [Fact]
    public void Migrate_Rotted_CarriesTheSignalsOwnCureOnASubLineUnderTheRow()
    {
        // The cure is the one the signal minted, not a second sentence composed here: status and the check
        // report would otherwise be two places to correct the same advice. The row above keeps its anchored
        // shape — the line rides under it, which is what keeps the quoted-count gate matching.
        IReadOnlyList<string> lines = StatusFormatter.Lines(
            new CheckReport(
            [
                Result(
                    Model.Rule("data-access/no-inline-sql"), RuleStatus.Passed, stale: 2, captured: true,
                    inertHint: "A trailing `.*` covers the namespace itself.")
            ]));

        lines[1]
            .ShouldBe(
                "  not measured: the rule's selection matched nothing, so these entries are untested, not "
                + "fixed. A trailing `.*` covers the namespace itself.");
    }

    [Fact]
    public void Migrate_RottedWithAnEmptyBaseline_IsNotPromotable()
    {
        // Burned to zero and never measured are the same four counts, and only one of them is progress:
        // without the predicate on Promotable this row would invite enforcing a rule that measures nothing.
        IReadOnlyList<string> lines = StatusFormatter.Lines(
            new CheckReport(
            [
                Result(
                    Model.Rule("data-access/no-inline-sql"), RuleStatus.Passed, captured: true,
                    inertHint: "THE-TARGET-CURE")
            ]));

        lines[0]
            .ShouldBe("pass data-access/no-inline-sql (migrate) — 0 grandfathered remaining");
        lines[0]
            .ShouldNotContain("promotable");
    }

    [Fact]
    public void Migrate_RottedAndUncaptured_StatesTheStateAndWithholdsTheInitNudgeToo()
    {
        // The third arm, and the same reasoning as the acceptance nudge: capturing from a rule that measured
        // nothing signs off "zero debt" nobody saw, which is why the verb refuses it. What is left is the
        // state, which is true whatever the selection did.
        IReadOnlyList<string> lines = StatusFormatter.Lines(
            new CheckReport(
            [
                Result(
                    Model.Rule("data-access/no-inline-sql"), RuleStatus.Failed,
                    emptySubjectHint: "THE-SUBJECT-CURE")
            ]));

        lines[0]
            .ShouldBe("FAIL data-access/no-inline-sql (migrate) — no baseline captured (1 current violation)");
        lines[0]
            .ShouldNotContain("baseline --init");
        lines[1]
            .ShouldStartWith("  not measured:");
    }

    [Fact]
    public void Enforce_Rotted_KeepsItsRowToItself()
    {
        // The sub-line is keyed on the baseline, like the per-cell split: its sentence is about entries and an
        // Enforce rule has none. Its row is also not the misleading one — it reads the warning count it always
        // read, and check is where the cure for a rule the reader has to fix actually belongs. Two of the
        // violated fixture's rules are exactly this shape, so the gate is load-bearing on a real report.
        IReadOnlyList<string> lines = StatusFormatter.Lines(
            new CheckReport(
            [
                Result(
                    Model.Rule("layering/billing-independent"), RuleStatus.Passed, warnings: 1,
                    inertHint: "THE-TARGET-CURE")
            ]));

        lines[0]
            .ShouldBe("pass layering/billing-independent — 2 warnings");
        lines.Count.ShouldBe(2, "the rule's row and the summary, and nothing between them");
    }

    [Fact]
    public void Summary_RottedAndMeasuredRules_PartitionTheUnmatchedEntriesBetweenTwoTerms()
    {
        // The roll-up stops counting rot as fixed debt, and the two terms partition every unmatched entry
        // between them: two were really fixed, twelve were never tested. One total for both read the rotted
        // rule's whole section as a migration somebody had finished.
        var report = new CheckReport(
        [
            Result(Model.Rule("data-access/no-inline-sql"), RuleStatus.Passed, stale: 2, captured: true),
            Result(
                Model.Rule("layering/cuts-not-circular"), RuleStatus.Passed, stale: 12, captured: true,
                inertHint: "THE-TARGET-CURE")
        ]);

        StatusFormatter.Lines(report)
            .Last()
            .ShouldBe(
                "Checked 2 rules: 2 passed, 0 failed, 0 skipped. Burndown: 0 grandfathered remaining, "
                + "2 fixed awaiting acceptance, 12 not measured.");
    }

    [Fact]
    public void Summary_EveryRuleMeasured_KeepsTheBurndownItAlwaysPrinted()
    {
        // The term extinguishes itself, like the two measure clauses beside it: a run with no rot in it reads
        // byte for byte as it did before rot had a word.
        var report = new CheckReport(
            [Result(Model.Rule("data-access/no-inline-sql"), RuleStatus.Passed, stale: 2, captured: true)]);

        StatusFormatter.Lines(report)
            .Last()
            .ShouldNotContain("not measured");
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
        bool captured = false, string? skipReason = null, int sitesEach = 0, int shrunk = 0, int uncounted = 0,
        IReadOnlyList<Violation>? debt = null, string? inertHint = null, string? emptySubjectHint = null)
    {
        IReadOnlyList<Violation> remaining = debt
                                             ?? (sitesEach > 0
                                                 ? SitedDummies(grandfathered, sitesEach)
                                                 : SyntheticNodes.RuleErrors(grandfathered));

        // The two rotted shapes, each arriving the way the checker produces it: an inert target as a warning
        // on a rule that still passes, an empty subject as the one violation that failed it. Named after the
        // signal rather than after the row, because which of them a result carries is what decides its arm.
        List<CheckWarning> warned = Enumerable.Range(0, warnings)
            .Select(_ => new CheckWarning(CheckWarningKind.InertTarget, "w"))
            .ToList();
        if (inertHint is not null) warned.Add(new CheckWarning(CheckWarningKind.InertTarget, "inert", hint: inertHint));

        IReadOnlyList<Violation> red = emptySubjectHint is null
            ? SyntheticNodes.RuleErrors(violations)
            : [Violation.EmptySubject("matched nothing", emptySubjectHint)];

        return new RuleResult(
            rule,
            status,
            red,
            warned,
            skipReason,
            remaining,
            new RatchetMeasure(stale, shrunk, uncounted),
            captured);
    }

    /// <summary>
    ///     The site-carrying sibling: <paramref name="count" /> stand-ins of <paramref name="sites" /> distinct
    ///     <c>file:line</c> sites apiece — the shape a real grandfathered pair has, and the only one that
    ///     reaches the site total. <paramref name="cell" /> labels them as a family verb's per-cell walk
    ///     would, which is what the burndown's sub-line groups by; unlabelled by default, like every
    ///     violation of every rule whose subject is not a family.
    /// </summary>
    /// <remarks>
    ///     Their kind is nothing the burndown reads — it counts pairs and sums sites — so these stay the
    ///     cheapest violation that carries a site, exactly as the ratchet rows' stand-ins do.
    /// </remarks>
    private static IReadOnlyList<Violation> SitedDummies(int count, int sites, string? cell = null)
    {
        return Enumerable.Range(0, count)
            .Select(index => Violation.Shape(
                    SyntheticNodes.Type($"App.Subject{cell}{index}"),
                    SyntheticNodes.Sites($"Subject{cell}{index}.cs", sites))
                .InCell(cell))
            .ToList();
    }
}
