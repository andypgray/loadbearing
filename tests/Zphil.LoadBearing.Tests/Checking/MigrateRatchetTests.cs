using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Tests.Checking.Targets;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     The Migrate ratchet (DESIGN.md §5, Phase 5): a Migrate rule evaluates like Enforce, then its
///     violations are partitioned against a baseline keyed by stable symbol IDs (GRAMMAR §4.3). In the
///     baseline = grandfathered (pass); not in it — including new code in the old pattern and a new
///     forbidden target from a grandfathered source — = red. <c>EmptySubject</c>/<c>RuleError</c> are
///     never baselinable; an inert target still warns-and-passes; the two-arg overload sees no baselines;
///     an Enforce rule ignores the index entirely.
/// </summary>
public sealed class MigrateRatchetTests
{
    // A controller opening the data layer directly — one forbidden edge (OldController -> App.Data.Db).
    private const string OneController = """
                                         namespace App.Web { public class OldController { public App.Data.Db Load() => new App.Data.Db(); } }
                                         namespace App.Data { public class Db {} }
                                         """;

    // The same source's rule: Web controllers must not reference the data layer.
    private static void NoDataAccess(Arch arch)
    {
        arch.Rule("data/x")
            .Migrate(
                "Controllers open the data layer directly (legacy Active Record style).",
                arch.Namespace("App.Web.*").WithSuffix("Controller").MustNotReference(arch.Namespace("App.Data.*")))
            .Because("Repository pattern for testability.");
    }

    private static BaselineIndex Index(string ruleId, params BaselineEntry[] entries)
    {
        return new BaselineIndex(new Dictionary<string, RuleBaseline>(StringComparer.Ordinal)
        {
            [ruleId] = new(entries)
        });
    }

    [Fact]
    public void Check_MigrateViolationInBaseline_PassesWithGrandfathered()
    {
        BaselineIndex index = Index("data/x", BaselineEntry.ForEdge("T:App.Web.OldController", "T:App.Data.Db"));

        RuleResult result = Checker.Run(OneController, index, NoDataAccess).Single();

        result.Status.ShouldBe(RuleStatus.Passed);
        result.Violations.ShouldBeEmpty();
        result.Grandfathered.Count.ShouldBe(1);
        result.BaselineCaptured.ShouldBeTrue();
    }

    [Fact]
    public void Check_MigrateViolationNotInBaseline_FailsRed()
    {
        // Captured section, but this edge is not in it — new code in the old pattern is red.
        RuleResult result = Checker.Run(OneController, Index("data/x"), NoDataAccess).Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.Violations.Count.ShouldBe(1);
        result.Grandfathered.ShouldBeEmpty();
        result.BaselineCaptured.ShouldBeTrue();
    }

    [Fact]
    public void Check_GrandfatheredSourceWithNewForbiddenTarget_FailsRed()
    {
        // Pair identity (GRAMMAR §4.3): OldController is grandfathered for Db, but its NEW edge to Cache is red.
        const string source = """
                              namespace App.Web
                              {
                                  public class OldController
                                  {
                                      public App.Data.Db A() => new App.Data.Db();
                                      public App.Data.Cache B() => new App.Data.Cache();
                                  }
                              }
                              namespace App.Data { public class Db {} public class Cache {} }
                              """;
        BaselineIndex index = Index("data/x", BaselineEntry.ForEdge("T:App.Web.OldController", "T:App.Data.Db"));

        RuleResult result = Checker.Run(source, index, NoDataAccess).Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.ReferencePairs().ShouldBe(["App.Web.OldController -> App.Data.Cache"]);
        result.Grandfathered.Count.ShouldBe(1);
    }

    [Fact]
    public void Check_MigrateShapeViolation_GrandfathersBySubjectId()
    {
        const string source = "namespace App { public class GoodHandler {} public class BadThing {} }";
        BaselineIndex index = Index("naming/x", BaselineEntry.ForSubject("T:App.BadThing"));

        RuleResult result = Checker.Run(source, index, arch =>
                arch.Rule("naming/x")
                    .Migrate("Types are inconsistently named.", arch.Namespace("App.*").MustHaveSuffix("Handler"))
                    .Because("Handler discovery is convention-based."))
            .Single();

        result.Status.ShouldBe(RuleStatus.Passed);
        result.Grandfathered.Count.ShouldBe(1);
        result.Violations.ShouldBeEmpty();
    }

    [Fact]
    public void Check_StaleBaselineEntry_CountsWithoutFailing()
    {
        // Old is grandfathered and present; the Ghost entry matches no current violation → stale.
        BaselineIndex index = Index(
            "data/x",
            BaselineEntry.ForEdge("T:App.Web.OldController", "T:App.Data.Db"),
            BaselineEntry.ForEdge("T:App.Web.GhostController", "T:App.Data.Db"));

        RuleResult result = Checker.Run(OneController, index, NoDataAccess).Single();

        result.Status.ShouldBe(RuleStatus.Passed);
        result.Grandfathered.Count.ShouldBe(1);
        result.StaleBaselineEntries.ShouldBe(1);
    }

    [Fact]
    public void Check_UncapturedMigrateRule_AllViolationsRedAndCapturedFalse()
    {
        RuleResult result = Checker.Run(OneController, BaselineIndex.Empty, NoDataAccess).Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.Violations.Count.ShouldBe(1);
        result.BaselineCaptured.ShouldBeFalse();
    }

    [Fact]
    public void Check_CapturedEmptySection_PassesAndCapturedTrue()
    {
        // A clean controller with an empty (zero-debt) section — the promotable state.
        const string source = """
                              namespace App.Web { public class CleanController { public string M() => ""; } }
                              namespace App.Data { public class Db {} }
                              """;

        RuleResult result = Checker.Run(source, Index("data/x"), NoDataAccess).Single();

        result.Status.ShouldBe(RuleStatus.Passed);
        result.BaselineCaptured.ShouldBeTrue();
        result.Grandfathered.ShouldBeEmpty();
        result.StaleBaselineEntries.ShouldBe(0);
    }

    [Fact]
    public void Check_MigrateEmptySubject_IsRedNeverBaselinable()
    {
        RuleResult result = Checker.Run("namespace App { public class X {} }", Index("data/x"), arch =>
                arch.Rule("data/x")
                    .Migrate(
                        "old",
                        arch.Namespace("App.Nowhere.*").WithSuffix("Controller").MustNotReference(arch.Namespace("App.Data.*")))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.Violations.Single().Kind.ShouldBe(ViolationKind.EmptySubject);
        result.Grandfathered.ShouldBeEmpty();
    }

    [Fact]
    public void Check_MigrateRuleError_IsFailedNeverBaselinable()
    {
        RuleResult result = Checker.Run(Sources.Hierarchy, Index("data/x"), arch =>
                arch.Rule("data/x")
                    .Migrate("old", arch.Types.MustNotReference(typeof(IHandler<Order>)))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.Violations.Single().Kind.ShouldBe(ViolationKind.RuleError);
        result.Grandfathered.ShouldBeEmpty();
    }

    [Fact]
    public void Check_MigrateInertTarget_WarnsAndPasses()
    {
        RuleResult result = Checker.Run("namespace App.Web { public class HomeController {} }", Index("data/x"), arch =>
                arch.Rule("data/x")
                    .Migrate(
                        "old",
                        arch.Namespace("App.Web.*").WithSuffix("Controller").MustNotReference(arch.Namespace("App.Ghost.*")))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Passed);
        result.Warnings.Single().Kind.ShouldBe(CheckWarningKind.InertTarget);
    }

    [Fact]
    public void Check_TwoArgOverload_TreatsBaselinesAsEmpty()
    {
        RuleResult result = Checker.Run(OneController, NoDataAccess).Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.BaselineCaptured.ShouldBeFalse();
    }

    [Fact]
    public void Check_EnforceRule_IgnoresBaselineIndex()
    {
        // Even an index that carries the exact edge does not grandfather an Enforce rule.
        BaselineIndex index = Index("layer/x", BaselineEntry.ForEdge("T:App.Web.OldController", "T:App.Data.Db"));

        RuleResult result = Checker.Run(OneController, index, arch =>
                arch.Rule("layer/x")
                    .Enforce(arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Data.*")))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.Grandfathered.ShouldBeEmpty();
    }
}