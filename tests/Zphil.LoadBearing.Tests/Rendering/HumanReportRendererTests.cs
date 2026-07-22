using Shouldly;
using Xunit;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Tests.Checking;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Rendering;

/// <summary>
///     The human failure-text renderer's violation arms (<see cref="HumanReportRenderer" />, the shared
///     CLI + xUnit-adapter surface): the unlocated <c>error:</c> (RuleError) and empty-subject lines, the
///     site-less Shape fallback, the unlocated-before-located ordering, and the <c>Render</c> summary tail.
///     Pinned strings are the spec.
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
        Constraint constraint = arch.Namespace("App.Async.*").Methods.Returning(typeof(Task<int>)).MustHaveSuffix("Async");
        var model = new ArchitectureModel(
            [new ArchRule("naming/x", Posture.Enforce, "b", null, "sentence", constraint, null, null)], []);
        RuleResult result = ArchChecker.Check(model, AsyncModel).Single();

        string block = HumanReportRenderer.RuleBlock(result, Directory.GetCurrentDirectory());

        result.Violations.Single().Kind.ShouldBe(ViolationKind.RuleError);
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
                .Because("b")).Single();

        string block = HumanReportRenderer.RuleBlock(result, Directory.GetCurrentDirectory());

        result.Violations.Single().Kind.ShouldBe(ViolationKind.EmptySubject);
        block.ShouldContain("The subject selection matched no solution-declared types.");
    }

    [Fact]
    public void RuleBlock_ShapeSubjectWithoutDeclarationSites_RendersUnlocatedFullName()
    {
        // A Shape subject with no DeclarationSites has no file:line, so the renderer emits its bare FullName as
        // an unlocated line (HumanReportRenderer.cs:101-102) rather than a located `path:line — …` line.
        TypeNode subject = Node("App.Orphan");
        var result = new RuleResult(
            EnforceRule("shape/x"), RuleStatus.Failed, [Violation.Shape(subject, [])], [], null, [], 0, false);

        string block = HumanReportRenderer.RuleBlock(result, Directory.GetCurrentDirectory());

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
            EnforceRule("di/x"), RuleStatus.Failed, [construction], [], null, [], 0, false);

        string block = HumanReportRenderer.RuleBlock(result, Directory.GetCurrentDirectory());

        result.Violations.Single().Kind.ShouldBe(ViolationKind.Construction);
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
            EnforceRule("ex/x"), RuleStatus.Failed, [catchViolation], [], null, [], 0, false);

        string block = HumanReportRenderer.RuleBlock(result, Directory.GetCurrentDirectory());

        result.Violations.Single().Kind.ShouldBe(ViolationKind.Catch);
        block.ShouldContain("App.Handler catches Errors.DbError");
        block.ShouldContain(":9 — App.Handler catches Errors.DbError");
    }

    [Fact]
    public void RuleBlock_ThrowViolation_RendersThrowsLineAtThrowSite()
    {
        // Likewise the Throw arm: a missing case would silently render nothing. Pin `Source throws Target` at the
        // `throw` site (GRAMMAR §4.8).
        Violation throwViolation = Violation.Throw(
            Node("App.Service"), Node("Errors.InfraError"), [new SourceLocation("Service.cs", 14)]);
        var result = new RuleResult(
            EnforceRule("ex/x"), RuleStatus.Failed, [throwViolation], [], null, [], 0, false);

        string block = HumanReportRenderer.RuleBlock(result, Directory.GetCurrentDirectory());

        result.Violations.Single().Kind.ShouldBe(ViolationKind.Throw);
        block.ShouldContain("App.Service throws Errors.InfraError");
        block.ShouldContain(":14 — App.Service throws Errors.InfraError");
    }

    [Fact]
    public void RuleBlock_MixedUnlocatedAndLocated_EmitsUnlocatedBeforeLocated()
    {
        // Every unlocated line (EmptySubject/RuleError/site-less Shape) is emitted before the file-ordered
        // located lines, regardless of input order (HumanReportRenderer.cs:121-127).
        TypeNode located = Node("App.Located", new SourceLocation("Located.cs", 7));
        var result = new RuleResult(
            EnforceRule("shape/x"), RuleStatus.Failed,
            [Violation.Shape(located, []), Violation.EmptySubject("UNLOCATED-MARKER")], [], null, [], 0, false);

        string block = HumanReportRenderer.RuleBlock(result, Directory.GetCurrentDirectory());

        // The unlocated EmptySubject detail precedes the located Shape's `path:line — App.Located`, though the
        // located violation was listed first.
        block.IndexOf("UNLOCATED-MARKER", StringComparison.Ordinal)
            .ShouldBeLessThan(block.IndexOf("App.Located", StringComparison.Ordinal));
    }

    [Fact]
    public void Render_MultiRuleReport_WritesSummaryTail()
    {
        var report = new CheckReport(
        [
            new RuleResult(EnforceRule("r/pass"), RuleStatus.Passed, [], [], null, [], 0, false),
            new RuleResult(EnforceRule("r/fail"), RuleStatus.Failed, [Violation.RuleError("boom")], [], null, [], 0, false),
            new RuleResult(EnforceRule("r/skip"), RuleStatus.Skipped, [], [], "no --diff-base diff context", [], 0, false)
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

    // A shallow TypeNode standing in for a Shape subject: the renderer reads only its FullName and
    // DeclarationSites, so the remaining scalar facts are inert placeholders.
    private static TypeNode Node(string fullName, params SourceLocation[] sites)
    {
        var node = new TypeNode(
            fullName, "T:" + fullName, fullName, string.Empty, TypeKind.Class, Accessibility.Public,
            false, false, false, false, "TestProject", false);
        node.DeclarationSites = sites;
        return node;
    }
}