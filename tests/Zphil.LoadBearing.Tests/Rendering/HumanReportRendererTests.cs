using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Hosting;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Tests.Checking;
using Zphil.LoadBearing.Tests.Extraction;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Rendering;

/// <summary>
///     The human failure-text renderer's violation arms (<see cref="HumanReportRenderer" />, the shared
///     CLI + xUnit-adapter surface): the unlocated <c>error:</c> (RuleError) and empty-subject lines, the
///     site-less Shape fallback, the unlocated-before-located ordering, the ratchet's grown trailer, the
///     dragons lines beneath a fired scope tripwire, and the <c>Render</c> summary tail — down to how it
///     inflects its counts and files a warned rule under <c>passed</c>. Two gates on a rule that reached no
///     verdict about the code are here too: the authored <c>fix:</c> is withheld, and the
///     <c>baseline --init</c> hint stays withheld for every violation with no identity to record. Pinned
///     strings are the spec.
/// </summary>
public sealed class HumanReportRendererTests
{
    private const string AsyncSource = """
                                       namespace App.Async
                                       {
                                           using System.Threading.Tasks;
                                           public class HomeController
                                           {
                                               public Task<int> Load() => Task.FromResult(0);
                                           }
                                       }
                                       """;

    private static readonly CodebaseModel AsyncModel = CompilationFactory.Extract(AsyncSource);

    // The shared/utilities caution both the fired and the silent row are about — the same scope, so the
    // only difference between them is whether a warning reached the block.
    private static readonly ArchRule SharedUtilitiesTripwire = Checker.Tripwire(arch =>
        arch.Scope("shared/utilities")
            .Caution(arch.Namespace("MyApp.Shared.*"))
            .Dragons("Argument order is load-bearing.")
            .Because("Every caller passes them positionally."));

    [Fact]
    public void RuleBlock_RuleError_RendersErrorPrefixedDetailLine()
    {
        // A hand-built model bypasses spec-build's closed-generic refusal (GRAMMAR §8 item 14) to reach the
        // checker's RuleError backstop; the renderer's RuleError arm prefixes the detail with "error: ".
        var arch = new Arch();
        Constraint constraint = arch.Namespace("App.Async.*").Methods.Returning(typeof(Task<int>))
            .MustHaveSuffix("Async");
        ArchitectureModel model = Checker.HandBuilt("naming/x", constraint);
        RuleResult result = ArchChecker.Check(model, AsyncModel)
            .Single();

        string block = result.HumanBlock();

        result.Violations.ShouldHaveSingleItem()
            .Kind.ShouldBe(ViolationKind.RuleError);
        block.ShouldContain(
            "error: `Task<Int32>` is a closed generic construction; member return-type matching is definition-level. " +
            "Anchor on the open definition instead.");
    }

    [Fact]
    public void RuleBlock_EmptySubject_RendersMatchedNoTypesDetailLine()
    {
        RuleResult result = Checker.Run(
                "namespace App { public class Foo {} }",
                arch => arch.Rule("naming/x")
                    .Enforce(arch.Namespace("Nowhere.*").MustHavePrefix("I"))
                    .Because("b"))
            .Single();

        string block = result.HumanBlock();

        result.Violations.ShouldHaveSingleItem()
            .Kind.ShouldBe(ViolationKind.EmptySubject);
        block.ShouldContain("The subject selection matched no solution-declared types.");
    }

    [Fact]
    public void RuleBlock_EmptySubject_RendersItsCureOnAHintLineUnderTheDiagnosis()
    {
        RuleResult result = Checker.Run(
                "namespace App { public class Foo {} }",
                arch => arch.Rule("naming/x")
                    .Enforce(arch.Namespace("Nowhere.*").MustHavePrefix("I"))
                    .Because("b"))
            .Single();

        string block = result.HumanBlock();

        block.ShouldContain("hint: A trailing `.*` covers the namespace itself");
        block.IndexOf("matched no solution-declared types", StringComparison.Ordinal)
            .ShouldBeLessThan(block.IndexOf("hint:", StringComparison.Ordinal));
    }

    [Fact]
    public void RuleBlock_SeveralEmptyPartsSharingOneCure_PrintItOnce()
    {
        // A union with two empty parts names both, then says once what they share: a reader who has met the
        // sentence learns nothing from meeting it again, and the diagnosis lines are what differ.
        var result = new RuleResult(
            EnforceRule("union/x"), RuleStatus.Failed,
            [
                Violation.EmptySubject("the first part matched nothing", "SHARED-CURE"),
                Violation.EmptySubject("the second part matched nothing", "SHARED-CURE")
            ], [], null, []);

        string block = result.HumanBlock();

        block.ShouldSatisfyAllConditions(
            () => block.ShouldContain("the first part matched nothing"),
            () => block.ShouldContain("the second part matched nothing"),
            () => (block.Split("hint: SHARED-CURE").Length - 1).ShouldBe(1, block));
    }

    [Fact]
    public void RuleBlock_InertTargetWarning_RendersItsCureUnderTheWarning()
    {
        RuleResult result = Checker.Run(
                "namespace App.Domain { public class Foo {} }",
                arch => arch.Rule("inert/x")
                    .Enforce(arch.Namespace("App.Domain.*").MustNotReference(arch.Namespace("App.Ghost.*")))
                    .Because("b"))
            .Single();

        string block = result.HumanBlock();

        block.ShouldContain("warning: This rule is inert");
        block.IndexOf("warning:", StringComparison.Ordinal)
            .ShouldBeLessThan(block.IndexOf("hint:", StringComparison.Ordinal));
    }

    [Fact]
    public void RuleBlock_RatchetedRuleFailingOnlyOnAnEmptySubject_OmitsTheBaselineHint()
    {
        // `baseline --init` grandfathers observed violations, and an empty subject has no identity to
        // record — so that hint is an instruction which silently does nothing, and printing it beside the
        // cure that works leaves two `hint:` lines disagreeing about what to do.
        var result = new RuleResult(
            MigrateRule("data/x"), RuleStatus.Failed,
            [Violation.EmptySubject("matched nothing", "THE-REAL-CURE")], [], null, []);

        string block = result.HumanBlock();

        block.ShouldSatisfyAllConditions(
            () => block.ShouldNotContain("baseline --init"),
            () => block.ShouldContain("hint: THE-REAL-CURE"));
    }

    [Fact]
    public void RuleBlock_FailingOnlyOnAnEmptySubject_KeepsTheReasonAndWithholdsTheFix()
    {
        // The fix advises correcting a violation of the rule, and an empty subject is not one: the rule never
        // ran, so the advice is for a situation that did not arise, sitting directly above the hint that says
        // what actually went wrong. The reason stays — it is a fact about the rule whatever happened to it,
        // and the xunit adapter's failure message is this same block.
        var result = new RuleResult(
            FixedRule("naming/x"), RuleStatus.Failed,
            [Violation.EmptySubject("matched nothing", "THE-REAL-CURE")], [], null, []);

        string block = result.HumanBlock();

        block.ShouldSatisfyAllConditions(
            () => block.ShouldContain("because: A reader cannot find the type."),
            () => block.ShouldNotContain("fix: Rename it."),
            () => block.ShouldContain("hint: THE-REAL-CURE"));
    }

    [Fact]
    public void RuleBlock_FailingOnlyOnARuleError_WithholdsTheFixToo()
    {
        // The same question, the same answer: a predicate that threw reached no verdict about the code either,
        // so the gate is keyed on that rather than on the one kind the empty-subject case named.
        var result = new RuleResult(
            FixedRule("naming/x"), RuleStatus.Failed, [Violation.RuleError("boom")], [], null, []);

        string block = result.HumanBlock();

        block.ShouldSatisfyAllConditions(
            () => block.ShouldContain("because: A reader cannot find the type."),
            () => block.ShouldNotContain("fix: Rename it."),
            () => block.ShouldContain("error: boom"));
    }

    [Fact]
    public void RuleBlock_FailingOnTheCode_StillPrintsTheFix()
    {
        // The control: a real finding is exactly what the fix is advice for, so the block a reader has always
        // got is unchanged. Without this row the gate could pass by withholding the fix from everything.
        var result = new RuleResult(
            FixedRule("naming/x"), RuleStatus.Failed, [Violation.Shape(SyntheticNodes.Type("App.Orphan"), [])],
            [], null, []);

        result.HumanBlock()
            .ShouldContain("fix: Rename it.");
    }

    [Fact]
    public void RuleBlock_RatchetedRuleThatErrored_OmitsTheBaselineHintAsWell()
    {
        // The latent half of the same defect. --init grandfathers by identity, and a rule error has none
        // either (GRAMMAR §4.3) — so a ratcheted, errored rule was told to run a command that would silently
        // record nothing, which is the instruction the empty-subject gate removed for its sibling kind. Keying
        // the gate on the identity rather than on the kinds that lack one is what covered this without anyone
        // having to remember it.
        var result = new RuleResult(
            MigrateRule("data/x"), RuleStatus.Failed, [Violation.RuleError("boom")], [], null, []);

        result.HumanBlock()
            .ShouldNotContain("baseline --init");
    }

    [Fact]
    public void RuleBlock_ShapeSubjectWithoutDeclarationSites_RendersUnlocatedFullName()
    {
        // A Shape violation carrying no site has no file:line, so the renderer emits its bare FullName as
        // an unlocated line (HumanReportRenderer.ViolationLines) rather than a located `path:line — …` line.
        TypeNode subject = SyntheticNodes.Type("App.Orphan");
        var result = new RuleResult(
            EnforceRule("shape/x"), RuleStatus.Failed, [Violation.Shape(subject, [])], [], null, []);

        string block = result.HumanBlock();

        block.ShouldContain("App.Orphan");
        block.ShouldEndWith("App.Orphan"); // unlocated — no trailing `:line`
    }

    [Fact]
    public void RuleBlock_RuleWithCitation_RendersCitationLineBetweenBecauseAndFix()
    {
        // The failing rule's framing, in the order a reader needs it: the reason, the page it rests on, then
        // what to do about it. A rule citing nothing renders the block it always did.
        var cited = new ArchRule(
            "http/reuse-httpclient", Posture.Enforce, "A new client per call exhausts sockets.",
            "Inject IHttpClientFactory.", "s", null, null, null,
            "https://learn.microsoft.com/dotnet/fundamentals/networking/http/httpclient-guidelines");
        var result = new RuleResult(
            cited, RuleStatus.Failed, [Violation.Shape(SyntheticNodes.Type("App.Orphan"), [])], [], null, []);

        string block = result.HumanBlock();

        block.ShouldContain(
            "  because: A new client per call exhausts sockets.\n"
            + "  citation: https://learn.microsoft.com/dotnet/fundamentals/networking/http/httpclient-guidelines\n"
            + "  fix: Inject IHttpClientFactory.");

        var uncited = new RuleResult(
            EnforceRule("shape/x"), RuleStatus.Failed, [Violation.Shape(SyntheticNodes.Type("App.Orphan"), [])],
            [], null, []);
        uncited.HumanBlock()
            .ShouldNotContain("citation:");
    }

    [Fact]
    public void RuleBlock_ConstructionViolation_RendersConstructsLineAtNewSite()
    {
        // ViolationLines has no default arm, so a missing Construction case would render nothing and a red
        // report would read green. Pin the located line text: `Source constructs Target` at the `new` site.
        var construction = Violation.Construction(
            SyntheticNodes.Type("App.Factory"), SyntheticNodes.Type("Widgets.Widget"),
            [new SourceLocation("Factory.cs", 12)]);
        var result = new RuleResult(
            EnforceRule("di/x"), RuleStatus.Failed, [construction], [], null, []);

        string block = result.HumanBlock();

        result.Violations.ShouldHaveSingleItem()
            .Kind.ShouldBe(ViolationKind.Construction);
        block.ShouldContain("App.Factory constructs Widgets.Widget");
        block.ShouldContain(":12 — App.Factory constructs Widgets.Widget");
    }

    [Fact]
    public void RuleBlock_CatchViolation_RendersCatchesLineAtCatchSite()
    {
        // ViolationLines has no default arm, so a missing Catch case would render nothing and a red report would
        // read green. Pin the located line text: `Source catches Target` at the `catch` site (GRAMMAR §4.8).
        Violation catchViolation = Violation.Catch(
            SyntheticNodes.Type("App.Handler"), SyntheticNodes.Type("Errors.DbError"),
            [new SourceLocation("Handler.cs", 9)]);
        var result = new RuleResult(
            EnforceRule("ex/x"), RuleStatus.Failed, [catchViolation], [], null, []);

        string block = result.HumanBlock();

        result.Violations.ShouldHaveSingleItem()
            .Kind.ShouldBe(ViolationKind.Catch);
        block.ShouldContain("App.Handler catches Errors.DbError");
        block.ShouldContain(":9 — App.Handler catches Errors.DbError");
    }

    [Fact]
    public void RuleBlock_ExposeViolation_RendersExposesLineAtExposureSite()
    {
        // ViolationLines has no default arm, so a missing Expose case would render nothing and a red report would
        // read green. Pin the located line text: `Source exposes Target` at the exposing member's site (GRAMMAR §4.9).
        Violation exposeViolation = Violation.Expose(
            SyntheticNodes.Type("App.Facade"), SyntheticNodes.Type("Secrets.Secret"),
            [new SourceLocation("Facade.cs", 5)]);
        var result = new RuleResult(
            EnforceRule("ex/x"), RuleStatus.Failed, [exposeViolation], [], null, []);

        string block = result.HumanBlock();

        result.Violations.ShouldHaveSingleItem()
            .Kind.ShouldBe(ViolationKind.Expose);
        block.ShouldContain("App.Facade exposes Secrets.Secret");
        block.ShouldContain(":5 — App.Facade exposes Secrets.Secret");
    }

    [Fact]
    public void RuleBlock_ThrowViolation_RendersThrowsLineAtThrowSite()
    {
        // Likewise the Throw arm: a missing case would silently render nothing. Pin `Source throws Target` at the
        // `throw` site (GRAMMAR §4.8).
        Violation throwViolation = Violation.Throw(
            SyntheticNodes.Type("App.Service"), SyntheticNodes.Type("Errors.InfraError"),
            [new SourceLocation("Service.cs", 14)]);
        var result = new RuleResult(
            EnforceRule("ex/x"), RuleStatus.Failed, [throwViolation], [], null, []);

        string block = result.HumanBlock();

        result.Violations.ShouldHaveSingleItem()
            .Kind.ShouldBe(ViolationKind.Throw);
        block.ShouldContain("App.Service throws Errors.InfraError");
        block.ShouldContain(":14 — App.Service throws Errors.InfraError");
    }

    [Fact]
    public void RuleBlock_UnfilteredCatchViolation_RendersCatchesLineAtTheUnfilteredSiteOnly()
    {
        // MustNotCatchUnfiltered reuses the Catch arm, so it renders the same `Source catches Target` line — and
        // because its violation carries the edge's UNFILTERED sites, the filtered clause at line 9 is never
        // printed while the unfiltered one at line 11 is. That is what keeps every rendered line true under a
        // verb whose law the line itself does not state: nobody is pointed at a site the rule sanctions.
        const string source = """
                              namespace Errors { public class DbError : System.Exception {} }
                              namespace App
                              {
                                  public class Handler
                                  {
                                      public void Run(bool flag)
                                      {
                                          try { }
                                          catch (Errors.DbError) when (flag) { }
                                          try { }
                                          catch (Errors.DbError) { }
                                      }
                                  }
                              }
                              """;
        RuleResult result = Checker.Run(source, arch =>
                arch.Rule("ex/filter-catches")
                    .Enforce(arch.Namespace("App.*").MustNotCatchUnfiltered(arch.Namespace("Errors.*")))
                    .Because("A broad catch names what it expects."))
            .Single();

        string block = result.HumanBlock();

        result.Violations.ShouldHaveSingleItem()
            .Kind.ShouldBe(ViolationKind.Catch);
        block.ShouldContain("Test.cs:11 — App.Handler catches Errors.DbError");
        block.ShouldNotContain("Test.cs:9");
    }

    [Fact]
    public void RuleBlock_ForbiddenThrowViolation_RendersThrowsLineAtThrowSite()
    {
        // MustNotThrow reuses the Throw arm: the ban polarity renders the same `Source throws Target` line the
        // allow-list does, at the `throw` site — one arm serving the whole fact family.
        const string source = """
                              namespace App
                              {
                                  public class Service
                                  {
                                      public void Run() => throw new System.Exception();
                                  }
                              }
                              """;
        RuleResult result = Checker.Run(source, arch =>
                arch.Rule("ex/no-bare-throws")
                    .Enforce(arch.Namespace("App.*").MustNotThrow(typeof(Exception)))
                    .Because("Throw a type a caller can dispatch on."))
            .Single();

        string block = result.HumanBlock();

        result.Violations.ShouldHaveSingleItem()
            .Kind.ShouldBe(ViolationKind.Throw);
        block.ShouldContain("Test.cs:5 — App.Service throws System.Exception");
    }

    [Fact]
    public void RuleBlock_MixedUnlocatedAndLocated_EmitsUnlocatedBeforeLocated()
    {
        // Every unlocated line (EmptySubject/RuleError/site-less Shape) is emitted before the file-ordered
        // located lines, regardless of input order (HumanReportRenderer.ViolationLines).
        var site = new SourceLocation("Located.cs", 7);
        TypeNode located = SyntheticNodes.Type("App.Located", site);
        var result = new RuleResult(
            EnforceRule("shape/x"), RuleStatus.Failed,
            [Violation.Shape(located, [site]), Violation.EmptySubject("UNLOCATED-MARKER", "HINT-MARKER")], [], null,
            []);

        string block = result.HumanBlock();

        // The unlocated EmptySubject detail precedes the located Shape's `path:line — App.Located`, though the
        // located violation was listed first.
        block.IndexOf("UNLOCATED-MARKER", StringComparison.Ordinal)
            .ShouldBeLessThan(block.IndexOf("App.Located", StringComparison.Ordinal));
    }

    [Fact]
    public void RuleBlock_GrownPair_RendersRedSitesAndTheGrownTrailer()
    {
        // A grandfathered pair carrying one site more than its entry records. Every site is red, not just the
        // new one — which site is new is a diff question the count cannot answer — and the trailer is what
        // says why a pair the baseline names is red at all: the allowance beside what the run measured.
        BaselineIndex index = Checker.Baselines(
            "data/x",
            BaselineEntry.ForEdge("T:App.Web.OldController", "T:App.Data.Db")
                .WithSiteCount(1));

        RuleResult result = Checker.Run(Sources.TwoSiteController, index, arch =>
                arch.Rule("data/x")
                    .Migrate(
                        "Controllers open the data layer directly (legacy Active Record style).",
                        arch.Namespace("App.Web.*").WithSuffix("Controller").MustNotReference(arch.Namespace("App.Data.*")))
                    .Because("Repository pattern for testability."))
            .Single();

        string block = result.HumanBlock();

        block.ShouldContain("Test.cs:5 — App.Web.OldController references App.Data.Db");
        block.ShouldContain("Test.cs:6 — App.Web.OldController references App.Data.Db");
        block.ShouldContain("  grown: 1 grandfathered pair exceeded the baseline site count (1 baselined, 2 observed)");
        // The pair is red, so nothing passed for the grandfathered count to report — the trailer is the only
        // place the baseline is named.
        block.ShouldNotContain("grandfathered:");
    }

    [Fact]
    public void RuleBlock_SkippedRatchetedRule_IsTheHeaderAndTheReasonAndNothingElse()
    {
        // A ratcheted rule the run reached no verdict for has no burndown to print: the grandfathered count
        // and the baseline --init hint are both claims about a check that never happened. Pinned as the
        // whole block rather than as two absences, so any line that ever creeps in below the reason reds.
        const string reason = "'BillingOnly.slnf' narrowed this run.";
        ArchRule rule = MigrateRule("data/x");
        var result = new RuleResult(rule, RuleStatus.Skipped, [], [], reason, [], default, true);

        string block = result.HumanBlock();

        block.ShouldBe($"skip {rule.Id} — {rule.Sentence}\n  skipped: {reason}");
    }

    [Fact]
    public void RuleBlock_FiredQuarantineTripwire_PrintsTheDragonsOnceBeneathTheWarnings()
    {
        ArchRule tripwire = Checker.Tripwire(arch =>
            arch.Scope("legacy/billing")
                .Quarantine(arch.Namespace("MyApp.Legacy.Billing.*"))
                .Dragons("Banker's rounding happens at line-item level.")
                .Because("Replacement scheduled."));

        // Two changed files, one set of dragons: they are a fact about the scope, not about which file was
        // touched, so the reader meets them once however many warnings fired.
        string block = Touched(tripwire, CheckWarningKind.QuarantinedScopeTouched, "Calc.cs", "Rounding.cs")
            .HumanBlock();

        block.ShouldEndWith("\n  dragons: Banker's rounding happens at line-item level.");
        TextNormalization.Occurrences(block, "  dragons: ")
            .ShouldBe(1);
        block.ShouldNotContain("dragons-doc:");
    }

    [Fact]
    public void RuleBlock_FiredCautionTripwire_PrintsTheDragonsBeneathTheWarning()
    {
        Touched(SharedUtilitiesTripwire, CheckWarningKind.CautionedScopeTouched, "Helpers.cs")
            .HumanBlock()
            .ShouldEndWith("\n  dragons: Argument order is load-bearing.");
    }

    [Fact]
    public void RuleBlock_SilentTripwire_PrintsNoDragonsLine()
    {
        // Nothing in the change set touched the scope. The dragons are still true, but stating them under a
        // rule that did not fire puts them on every clean run of every check.
        var quiet = new RuleResult(SharedUtilitiesTripwire, RuleStatus.Passed, []);

        quiet.HumanBlock()
            .ShouldNotContain("dragons");
    }

    [Fact]
    public void RuleBlock_FiredTripwireWithBothDragonsForms_PrintsBothLinesInOrder()
    {
        ArchRule tripwire = Checker.Tripwire(arch =>
            arch.Scope("shared/utilities")
                .Caution(arch.Namespace("MyApp.Shared.*"))
                .Dragons("Argument order is load-bearing.")
                .DragonsDoc("arch/utilities-dragons.md")
                .Because("Every caller passes them positionally."));

        Touched(tripwire, CheckWarningKind.CautionedScopeTouched, "Helpers.cs")
            .HumanBlock()
            .ShouldEndWith("\n  dragons: Argument order is load-bearing.\n  dragons-doc: arch/utilities-dragons.md");
    }

    [Fact]
    public void RuleBlock_FiredTripwireWithDragonsDocOnly_PrintsTheDocLineAlone()
    {
        ArchRule tripwire = Checker.Tripwire(arch =>
            arch.Scope("shared/utilities")
                .Caution(arch.Namespace("MyApp.Shared.*"))
                .DragonsDoc("arch/utilities-dragons.md")
                .Because("Every caller passes them positionally."));

        string block = Touched(tripwire, CheckWarningKind.CautionedScopeTouched, "Helpers.cs")
            .HumanBlock();

        block.ShouldEndWith("\n  dragons-doc: arch/utilities-dragons.md");
        block.ShouldNotContain("  dragons: ");
    }

    [Fact]
    public void Render_MultiRuleReport_WritesSummaryTail()
    {
        var report = new CheckReport(
        [
            new RuleResult(EnforceRule("r/pass"), RuleStatus.Passed, [], [], null, []),
            new RuleResult(EnforceRule("r/fail"), RuleStatus.Failed, [Violation.RuleError("boom")], [], null, []),
            new RuleResult(EnforceRule("r/skip"), RuleStatus.Skipped, [], [], "no --diff-base diff context", [])
        ]);

        var writer = new StringWriter { NewLine = "\n" };
        HumanReportRenderer.Render(writer, report, Directory.GetCurrentDirectory());
        var output = writer.ToString();

        output.ShouldContain("Checked 3 rules: 1 passed, 1 failed, 1 skipped (1 violation, 0 warnings).");
        output.ShouldContain("skipped: no --diff-base diff context"); // the Skipped arm renders its reason
    }

    [Fact]
    public void Render_WarnedRule_IsCountedAsPassedAndItsWarningsInTheTail()
    {
        // A warned rule is a passed rule — the posture is the severity, and a warning never moves the exit
        // code — so 'warn' is the per-rule flag, the tail counts the warnings, and the passed figure stays
        // equal to what a check's JSON report calls rulesPassed.
        var report = new CheckReport(
        [
            new RuleResult(EnforceRule("r/pass"), RuleStatus.Passed, [], [], null, []),
            Touched(SharedUtilitiesTripwire, CheckWarningKind.CautionedScopeTouched, "Helpers.cs")
        ]);

        var writer = new StringWriter { NewLine = "\n" };
        HumanReportRenderer.Render(writer, report, Directory.GetCurrentDirectory());
        var output = writer.ToString();

        output.ShouldContain("warn shared/utilities/tripwire");
        output.ShouldContain("Checked 2 rules: 2 passed, 0 failed, 0 skipped (0 violations, 1 warning).");
        report.RulesPassed.ShouldBe(2); // the tail's passed figure, and the warned rule is one of the two
    }

    private static ArchRule EnforceRule(string id)
    {
        return new ArchRule(id, Posture.Enforce, "b", null, "s", null, null, null);
    }

    // A rule carrying both halves of the failing rule's framing, spelled out rather than placeholders: the
    // rows below assert which of them reached the page, so a reader of a red has to be able to tell the two
    // lines apart.
    private static ArchRule FixedRule(string id)
    {
        return new ArchRule(id, Posture.Enforce, "A reader cannot find the type.", "Rename it.", "s", null, null, null);
    }

    // A tripwire that fired: one warning per changed file, which is what the checker produces and what lets
    // a row ask whether two of them still print one set of dragons.
    private static RuleResult Touched(ArchRule tripwire, CheckWarningKind kind, params string[] files)
    {
        List<CheckWarning> warnings = files
            .Select(file => new CheckWarning(kind, $"Changed file '{file}' is inside the scope.", file))
            .ToList();

        return new RuleResult(tripwire, RuleStatus.Passed, [], warnings);
    }

    // A ratcheted rule, built through the spec so it carries a real BaselinePath — the one thing that makes
    // the renderer's ratchet lines reachable at all.
    private static ArchRule MigrateRule(string id)
    {
        return Checker.Model(arch => arch.Rule(id)
                .Migrate("old", arch.Namespace("App.*").MustHaveSuffix("X"))
                .Because("b"))
            .Rules.Single();
    }
}
