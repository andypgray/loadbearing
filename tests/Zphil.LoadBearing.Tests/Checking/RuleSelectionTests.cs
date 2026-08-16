using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     <see cref="ArchChecker.SelectRules" /> and the rule-list
///     <see cref="ArchChecker.Check(IReadOnlyList{ArchRule}, CodebaseModel, BaselineIndex, DiffContext?)" />
///     overload: which rules a narrowed run evaluates, and what the resulting report counts. A glob matches
///     the whole ID with <c>*</c> spanning the <c>/</c> separator and no implicit subtree, selection keeps
///     model order, and an empty glob list selects everything. Because selection decides what
///     <em>runs</em>, the report's counters are the subset's for free — and the verdict contract is
///     untouched: the run is green when the selected rules are, whatever the rest of the model says.
/// </summary>
public sealed class RuleSelectionTests
{
    // One forbidden edge in the source (App.Web.OldController -> App.Data.Db) and two inert-free
    // namespaces beside it, so exactly one of the four rules below is red and the other three pass clean.
    private const string ThreeNamespaces =
        """
        namespace App.Web { public class OldController { public App.Data.Db Load() => new App.Data.Db(); } }
        namespace App.Data { public class Db {} }
        namespace App.Legacy { public class Ledger { public App.Data.Db Book; } }
        """;

    [Fact]
    public void SelectRules_AreaGlob_SelectsThatAreaInModelOrder()
    {
        // Arrange
        ArchitectureModel model = Model();

        // Act
        var selected = ArchChecker.SelectRules(model, ["layering/*"]);

        // Assert
        selected.Select(rule => rule.Id)
            .ShouldBe(["layering/web-not-data", "layering/web-not-legacy"]);
    }

    [Fact]
    public void SelectRules_ExactId_SelectsThatRuleAndNotItsChildren()
    {
        // Arrange — legacy/billing and legacy/billing/containment differ by one ID segment.
        ArchitectureModel model = Model();

        // Act
        var selected = ArchChecker.SelectRules(model, ["legacy/billing"]);

        // Assert — no implicit subtree: an ID is a pattern over the whole ID, not a prefix.
        selected.Select(rule => rule.Id)
            .ShouldBe(["legacy/billing"]);
    }

    [Fact]
    public void SelectRules_Star_SpansTheIdSeparator()
    {
        // Arrange
        ArchitectureModel model = Model();

        // Act
        var selected = ArchChecker.SelectRules(model, ["legacy/*"]);

        // Assert — one '*' reaches across the slash, so an area glob takes a scope's children too.
        selected.Select(rule => rule.Id)
            .ShouldBe(["legacy/billing", "legacy/billing/containment"]);
    }

    [Fact]
    public void SelectRules_SeveralGlobs_UnionTheirMatchesInModelOrder()
    {
        // Arrange
        ArchitectureModel model = Model();

        // Act — an exact ID beside a glob; the authoring order decides the result order, not the argument order.
        var selected = ArchChecker.SelectRules(model, ["legacy/*", "layering/web-not-data"]);

        // Assert
        selected.Select(rule => rule.Id)
            .ShouldBe(["layering/web-not-data", "legacy/billing", "legacy/billing/containment"]);
    }

    [Fact]
    public void SelectRules_NoGlobs_SelectsEveryRule()
    {
        // Arrange
        ArchitectureModel model = Model();

        // Act
        var selected = ArchChecker.SelectRules(model, []);

        // Assert
        selected.Select(rule => rule.Id)
            .ShouldBe(model.Rules.Select(rule => rule.Id));
    }

    [Fact]
    public void SelectRules_GlobMatchingNothing_SelectsNoRule()
    {
        // Arrange
        ArchitectureModel model = Model();

        // Act
        var selected = ArchChecker.SelectRules(model, ["naming/*"]);

        // Assert — empty rather than everything, so a caller can refuse an unmatched filter rather than
        // silently running the whole model.
        selected.ShouldBeEmpty();
    }

    [Fact]
    public void Check_SelectedPassingRule_CountsTheSubsetAndReportsGreen()
    {
        // Arrange — the model's red rule (layering/web-not-data) is deliberately not selected.
        ArchitectureModel model = Model();
        CodebaseModel codebase = CompilationFactory.Extract(ThreeNamespaces);
        var selected = ArchChecker.SelectRules(model, ["layering/web-not-legacy"]);

        // Act
        CheckReport report = ArchChecker.Check(selected, codebase, BaselineIndex.Empty, null);

        // Assert — the counters are the subset's, and the unselected red rule does not reach the verdict.
        report.Results.Select(result => result.Rule.Id)
            .ShouldBe(["layering/web-not-legacy"]);
        report.RulesChecked.ShouldBe(1);
        report.RulesPassed.ShouldBe(1);
        report.RulesFailed.ShouldBe(0);
        report.ViolationCount.ShouldBe(0);
        report.HasViolations.ShouldBeFalse();
        report.Results.Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void Check_SelectedRedRule_CountsTheSubsetAndStillFails()
    {
        // Arrange
        ArchitectureModel model = Model();
        CodebaseModel codebase = CompilationFactory.Extract(ThreeNamespaces);
        var selected = ArchChecker.SelectRules(model, ["layering/web-not-data"]);

        // Act
        CheckReport report = ArchChecker.Check(selected, codebase, BaselineIndex.Empty, null);

        // Assert — narrowing changes what runs, never what a violation means.
        report.RulesChecked.ShouldBe(1);
        report.RulesFailed.ShouldBe(1);
        report.HasViolations.ShouldBeTrue();
        report.Results.Single()
            .ShouldHaveFailedWithEdge(ViolationKind.Reference, "App.Web.OldController", "App.Data.Db");
    }

    [Fact]
    public void Check_EverySelectedRule_MatchesTheWholeModelRun()
    {
        // Arrange
        ArchitectureModel model = Model();
        CodebaseModel codebase = CompilationFactory.Extract(ThreeNamespaces);
        var selected = ArchChecker.SelectRules(model, []);

        // Act
        CheckReport subset = ArchChecker.Check(selected, codebase, BaselineIndex.Empty, null);
        CheckReport whole = ArchChecker.Check(model, codebase, BaselineIndex.Empty, null);

        // Assert — the unfiltered narrow path is the whole-model path, so nothing about the default run moves.
        subset.Results.Select(result => result.Rule.Id)
            .ShouldBe(whole.Results.Select(result => result.Rule.Id));
        subset.RulesChecked.ShouldBe(whole.RulesChecked);
        subset.RulesFailed.ShouldBe(whole.RulesFailed);
        subset.ViolationCount.ShouldBe(whole.ViolationCount);
    }

    // Four rules across two areas, one of them red, with a child ID under legacy/billing so the
    // no-implicit-subtree row has something to not select.
    private static ArchitectureModel Model()
    {
        return Checker.Model(arch =>
        {
            arch.Rule("layering/web-not-data")
                .Enforce(arch.Namespace("App.Web.*")
                    .MustNotReference(arch.Namespace("App.Data.*")))
                .Because("Controllers must reach data through a repository.");

            arch.Rule("layering/web-not-legacy")
                .Enforce(arch.Namespace("App.Web.*")
                    .MustNotReference(arch.Namespace("App.Legacy.*")))
                .Because("The web layer must not grow new ties to the legacy ledger.");

            arch.Rule("legacy/billing")
                .Enforce(arch.Namespace("App.Legacy.*")
                    .MustNotReference(arch.Namespace("App.Web.*")))
                .Because("The ledger must not call back into the web layer.");

            arch.Rule("legacy/billing/containment")
                .Enforce(arch.Namespace("App.Data.*")
                    .MustNotReference(arch.Namespace("App.Legacy.*")))
                .Because("The data layer must not depend on the ledger it feeds.");
        });
    }
}
