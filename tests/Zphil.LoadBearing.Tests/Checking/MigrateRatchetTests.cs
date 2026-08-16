using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Tests.Checking.Targets;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     The Migrate ratchet: a Migrate rule evaluates like Enforce, then its
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

    // A stand-in for the reason a filtered run composes; its wording is pinned where it is minted.
    private const string NarrowingSkipReason = "'BillingOnly.slnf' narrowed this run: 2 projects were not checked.";

    // The same source's rule: Web controllers must not reference the data layer.
    private static void NoDataAccess(Arch arch)
    {
        arch.Rule("data/x")
            .Migrate(
                "Controllers open the data layer directly (legacy Active Record style).",
                arch.Namespace("App.Web.*")
                    .WithSuffix("Controller")
                    .MustNotReference(arch.Namespace("App.Data.*")))
            .Because("Repository pattern for testability.");
    }

    [Fact]
    public void Check_MigrateViolationInBaseline_PassesWithGrandfathered()
    {
        BaselineIndex index = Checker.Baselines("data/x", BaselineEntry.ForEdge("T:App.Web.OldController", "T:App.Data.Db"));

        RuleResult result = Checker.Run(OneController, index, NoDataAccess)
            .Single();

        result.ShouldHavePassed();
        result.Violations.ShouldBeEmpty();
        result.ShouldHaveGrandfathered(1);
        result.BaselineCaptured.ShouldBeTrue();
    }

    [Fact]
    public void Check_MigrateViolationNotInBaseline_FailsRed()
    {
        // Captured section, but this edge is not in it — new code in the old pattern is red.
        RuleResult result = Checker.Run(OneController, Checker.Baselines("data/x"), NoDataAccess)
            .Single();

        result.ShouldHaveFailed();
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
        BaselineIndex index = Checker.Baselines("data/x", BaselineEntry.ForEdge("T:App.Web.OldController", "T:App.Data.Db"));

        RuleResult result = Checker.Run(source, index, NoDataAccess)
            .Single();

        result.ShouldHaveFailed();
        result.ReferencePairs()
            .ShouldBe(["App.Web.OldController -> App.Data.Cache"]);
        result.ShouldHaveGrandfathered(1);
    }

    [Fact]
    public void Check_MigrateConstructionViolation_GrandfathersByEdgePair()
    {
        // A construction violation ratchets exactly like a reference: its identity is the (source, constructed)
        // type pair (GRAMMAR §4.3), so the same ForEdge entry grandfathers OldController `new`ing Db with zero
        // baseline-format change. (OneController's Load() does `new App.Data.Db()`.)
        BaselineIndex index = Checker.Baselines("data/x", BaselineEntry.ForEdge("T:App.Web.OldController", "T:App.Data.Db"));

        RuleResult result = Checker.Run(OneController, index, arch =>
                arch.Rule("data/x")
                    .Migrate(
                        "Controllers `new` the data layer directly (legacy Active Record style).",
                        arch.Namespace("App.Web.*")
                            .WithSuffix("Controller")
                            .MustNotConstruct(arch.Namespace("App.Data.*")))
                    .Because("Resolve via DI for testability."))
            .Single();

        result.ShouldHavePassed();
        result.Violations.ShouldBeEmpty();
        result.ShouldHaveGrandfathered(1);
        result.BaselineCaptured.ShouldBeTrue();
    }

    [Fact]
    public void Check_MigrateShapeViolation_GrandfathersBySubjectId()
    {
        const string source = "namespace App { public class GoodHandler {} public class BadThing {} }";
        BaselineIndex index = Checker.Baselines("naming/x", BaselineEntry.ForSubject("T:App.BadThing"));

        RuleResult result = Checker.Run(source, index, arch =>
                arch.Rule("naming/x")
                    .Migrate("Types are inconsistently named.", arch.Namespace("App.*")
                        .MustHaveSuffix("Handler"))
                    .Because("Handler discovery is convention-based."))
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveGrandfathered(1);
        result.Violations.ShouldBeEmpty();
    }

    [Fact]
    public void Check_StaleBaselineEntry_CountsWithoutFailing()
    {
        // Old is grandfathered and present; the Ghost entry matches no current violation → stale.
        BaselineIndex index = Checker.Baselines(
            "data/x",
            BaselineEntry.ForEdge("T:App.Web.OldController", "T:App.Data.Db"),
            BaselineEntry.ForEdge("T:App.Web.GhostController", "T:App.Data.Db"));

        RuleResult result = Checker.Run(OneController, index, NoDataAccess)
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveGrandfathered(1);
        result.StaleBaselineEntries.ShouldBe(1);
    }

    [Fact]
    public void Check_UncapturedMigrateRule_AllViolationsRedAndCapturedFalse()
    {
        RuleResult result = Checker.Run(OneController, BaselineIndex.Empty, NoDataAccess)
            .Single();

        result.ShouldHaveFailed();
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

        RuleResult result = Checker.Run(source, Checker.Baselines("data/x"), NoDataAccess)
            .Single();

        result.ShouldHavePassed();
        result.BaselineCaptured.ShouldBeTrue();
        result.Grandfathered.ShouldBeEmpty();
        result.StaleBaselineEntries.ShouldBe(0);
    }

    [Fact]
    public void Check_MigrateEmptySubject_IsRedNeverBaselinable()
    {
        RuleResult result = Checker.Run("namespace App { public class X {} }", Checker.Baselines("data/x"), arch =>
                arch.Rule("data/x")
                    .Migrate(
                        "old",
                        arch.Namespace("App.Nowhere.*")
                            .WithSuffix("Controller")
                            .MustNotReference(arch.Namespace("App.Data.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailed();
        result.Violations.ShouldHaveSingleItem()
            .Kind.ShouldBe(ViolationKind.EmptySubject);
        result.Grandfathered.ShouldBeEmpty();
    }

    [Fact]
    public void Check_MigrateEmptySubjectUnderNarrowing_SkipsWithoutStalingTheSection()
    {
        // The ratchet's half of the narrowing skip. Ratchet would match nothing against the captured
        // section and report both entries as fixed awaiting acceptance — for a rule this run never
        // measured — which is exactly the reduction 'baseline --accept-reductions' would then delete. So
        // the skip is minted before the ratchet fork and forces the counts to nothing, while
        // BaselineCaptured stays truthful: the section is real, it simply went unmeasured.
        BaselineIndex index = Checker.Baselines(
            "data/x",
            BaselineEntry.ForEdge("T:App.Web.OldController", "T:App.Data.Db"),
            BaselineEntry.ForEdge("T:App.Web.GhostController", "T:App.Data.Db"));
        var narrowing = new NarrowedUniverse(NarrowingSkipReason);

        RuleResult result = Checker.Run("namespace App { public class X {} }", index, narrowing, arch =>
                arch.Rule("data/x")
                    .Migrate(
                        "old",
                        arch.Namespace("App.Nowhere.*")
                            .WithSuffix("Controller")
                            .MustNotReference(arch.Namespace("App.Data.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveSkipped(NarrowingSkipReason);
        result.ShouldHaveGrandfathered(0);
        result.StaleBaselineEntries.ShouldBe(0);
        result.BaselineCaptured.ShouldBeTrue();
    }

    [Fact]
    public void Check_MigrateRuleError_IsFailedNeverBaselinable()
    {
        RuleResult result = Checker.Run(Sources.HierarchyModel, Checker.Baselines("data/x"), arch =>
                arch.Rule("data/x")
                    .Migrate("old", arch.Types.MustNotReference(typeof(IHandler<Order>)))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailed();
        result.Violations.ShouldHaveSingleItem()
            .Kind.ShouldBe(ViolationKind.RuleError);
        result.Grandfathered.ShouldBeEmpty();
    }

    [Fact]
    public void Check_MigrateInertTarget_WarnsAndPasses()
    {
        RuleResult result = Checker.Run("namespace App.Web { public class HomeController {} }", Checker.Baselines("data/x"), arch =>
                arch.Rule("data/x")
                    .Migrate(
                        "old",
                        arch.Namespace("App.Web.*")
                            .WithSuffix("Controller")
                            .MustNotReference(arch.Namespace("App.Ghost.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveWarnedInertTarget();
    }

    [Fact]
    public void Check_TwoArgOverload_TreatsBaselinesAsEmpty()
    {
        RuleResult result = Checker.Run(OneController, NoDataAccess)
            .Single();

        result.ShouldHaveFailed();
        result.BaselineCaptured.ShouldBeFalse();
    }

    [Fact]
    public void Check_EnforceRule_IgnoresBaselineIndex()
    {
        // Even an index that carries the exact edge does not grandfather an Enforce rule.
        BaselineIndex index = Checker.Baselines("layer/x", BaselineEntry.ForEdge("T:App.Web.OldController", "T:App.Data.Db"));

        RuleResult result = Checker.Run(OneController, index, arch =>
                arch.Rule("layer/x")
                    .Enforce(arch.Namespace("App.Web.*")
                        .MustNotReference(arch.Namespace("App.Data.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailed();
        result.Grandfathered.ShouldBeEmpty();
    }
}
