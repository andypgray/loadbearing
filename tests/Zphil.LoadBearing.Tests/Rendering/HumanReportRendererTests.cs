using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Hosting;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Tests.Checking;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Rendering;

/// <summary>
///     The human failure-text renderer's violation arms (<see cref="HumanReportRenderer" />, the shared
///     CLI + xUnit-adapter surface): the unlocated <c>error:</c> (RuleError) and empty-subject lines, the
///     site-less Shape fallback, the unlocated-before-located ordering, the ratchet's grown trailer, and the
///     <c>Render</c> summary tail. Pinned strings are the spec.
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

    [Fact]
    public void RuleBlock_RuleError_RendersErrorPrefixedDetailLine()
    {
        // A hand-built model bypasses spec-build's closed-generic refusal (GRAMMAR §8 item 14) to reach the
        // checker's RuleError backstop; the renderer's RuleError arm prefixes the detail with "error: ".
        var arch = new Arch();
        Constraint constraint = arch.Namespace("App.Async.*").Methods.Returning(typeof(Task<int>))
            .MustHaveSuffix("Async");
        var model = new ArchitectureModel(
            [new ArchRule("naming/x", Posture.Enforce, "b", null, "sentence", constraint, null, null)], []);
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
    public void RuleBlock_ShapeSubjectWithoutDeclarationSites_RendersUnlocatedFullName()
    {
        // A Shape violation carrying no site has no file:line, so the renderer emits its bare FullName as
        // an unlocated line (HumanReportRenderer.cs:151-152) rather than a located `path:line — …` line.
        TypeNode subject = Node("App.Orphan");
        var result = new RuleResult(
            EnforceRule("shape/x"), RuleStatus.Failed, [Violation.Shape(subject, [])], [], null, []);

        string block = result.HumanBlock();

        block.ShouldContain("App.Orphan");
        block.ShouldEndWith("App.Orphan"); // unlocated — no trailing `:line`
    }

    [Fact]
    public void RuleBlock_ConstructionViolation_RendersConstructsLineAtNewSite()
    {
        // ViolationLines has no default arm, so a missing Construction case would render nothing and a red
        // report would read green. Pin the located line text: `Source constructs Target` at the `new` site.
        var construction = Violation.Construction(
            Node("App.Factory"), Node("Widgets.Widget"), [new SourceLocation("Factory.cs", 12)]);
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
            Node("App.Handler"), Node("Errors.DbError"), [new SourceLocation("Handler.cs", 9)]);
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
            Node("App.Facade"), Node("Secrets.Secret"), [new SourceLocation("Facade.cs", 5)]);
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
            Node("App.Service"), Node("Errors.InfraError"), [new SourceLocation("Service.cs", 14)]);
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
        // located lines, regardless of input order (HumanReportRenderer.cs:177-185).
        var site = new SourceLocation("Located.cs", 7);
        TypeNode located = Node("App.Located", site);
        var result = new RuleResult(
            EnforceRule("shape/x"), RuleStatus.Failed,
            [Violation.Shape(located, [site]), Violation.EmptySubject("UNLOCATED-MARKER")], [], null, []);

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
        const string source = """
                              namespace App.Web
                              {
                                  public class OldController
                                  {
                                      public App.Data.Db A() => new App.Data.Db();
                                      public App.Data.Db B() => new App.Data.Db();
                                  }
                              }
                              namespace App.Data { public class Db {} }
                              """;
        BaselineIndex index = Checker.Baselines(
            "data/x",
            BaselineEntry.ForEdge("T:App.Web.OldController", "T:App.Data.Db")
                .WithSiteCount(1));

        RuleResult result = Checker.Run(source, index, arch =>
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

        output.ShouldContain("Checked 3 rules: 1 passed, 1 failed, 1 skipped (1 violations, 0 warnings).");
        output.ShouldContain("skipped: no --diff-base diff context"); // the Skipped arm renders its reason
    }

    private static ArchRule EnforceRule(string id)
    {
        return new ArchRule(id, Posture.Enforce, "b", null, "s", null, null, null);
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

    // A shallow TypeNode standing in for a Shape subject: the renderer reads only its FullName and
    // DeclarationSites, so the remaining scalar facts are inert placeholders.
    private static TypeNode Node(string fullName, params SourceLocation[] sites)
    {
        return new TypeNode(
            fullName, "T:" + fullName, fullName, string.Empty, TypeKind.Class, Accessibility.Public,
            false, false, false, false, false, "TestProject", false) { DeclarationSites = sites };
    }
}
