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
///     forbidden target from a grandfathered source — = red. An edge entry may also record how many
///     sites it grandfathers, and then the count decides too: more is growth and red, fewer is a
///     reduction that passes, and an entry recording nothing holds its pair at any size.
///     <c>EmptySubject</c>/<c>RuleError</c> are
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

    // The same forbidden edge (OldController -> App.Data.Db) reached from two distinct lines. Sites are
    // deduped per file:line, so this is two sites under one identity — the shape the measure counts.
    private const string TwoSiteController = """
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

    // One (source, caught) edge caught three times in one type: unfiltered once and behind a `when` filter
    // twice. MustNotCatch reports all three sites as evidence; MustNotCatchUnfiltered reports the one.
    private const string ThreeCatchHandler = """
                                             namespace Errors { public class DbError : System.Exception {} }
                                             namespace App
                                             {
                                                 public class Handler
                                                 {
                                                     public void Run(bool flag)
                                                     {
                                                         try { }
                                                         catch (Errors.DbError) { }
                                                         try { }
                                                         catch (Errors.DbError) when (flag) { }
                                                         try { }
                                                         catch (Errors.DbError) when (!flag) { }
                                                     }
                                                 }
                                             }
                                             """;

    // A stand-in for the reason a filtered run composes; its wording is pinned where it is minted.
    private const string NarrowingSkipReason = "'BillingOnly.slnf' narrowed this run: 2 projects were not checked.";

    // The same source's rule: Web controllers must not reference the data layer.
    private static void NoDataAccess(Arch arch)
    {
        arch.Rule("data/x")
            .Migrate(
                "Controllers open the data layer directly (legacy Active Record style).",
                arch.Namespace("App.Web.*").WithSuffix("Controller").MustNotReference(arch.Namespace("App.Data.*")))
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
        result.Violations.ShouldHaveSingleItem();
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

        result.ShouldHaveFailedWithEdges(ViolationKind.Reference, ["App.Web.OldController -> App.Data.Cache"]);
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
                        arch.Namespace("App.Web.*").WithSuffix("Controller")
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
                    .Migrate("Types are inconsistently named.", arch.Namespace("App.*").MustHaveSuffix("Handler"))
                    .Because("Handler discovery is convention-based."))
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveGrandfathered(1);
        result.Violations.ShouldBeEmpty();
    }

    [Fact]
    public void Check_MoreSitesThanTheEntryRecords_IsRedAsGrowth()
    {
        // The hole the measure closes: a second old-pattern site inside an already-grandfathered pair used
        // to ride in free, which is precisely where new code in the old pattern gets written — inside a
        // type whose surrounding code already does it.
        BaselineIndex index = Checker.Baselines(
            "data/x",
            BaselineEntry.ForEdge("T:App.Web.OldController", "T:App.Data.Db")
                .WithSiteCount(1));

        RuleResult result = Checker.Run(TwoSiteController, index, NoDataAccess)
            .Single();

        result.ShouldHaveFailedWithEdges(ViolationKind.Reference, ["App.Web.OldController -> App.Data.Db"]);
        result.ShouldHaveGrown(1);
        result.ShouldHaveGrandfathered(0);
        // Red with ALL its sites, not just the new one: which site is new is a diff question, and the count
        // is deliberately lossy — that lossiness is what buys immunity to line churn.
        result.Violations.ShouldHaveSingleItem()
            .Sites.Count.ShouldBe(2);
    }

    [Fact]
    public void Check_SitesWithinTheRecordedCount_PassesGrandfathered()
    {
        BaselineIndex index = Checker.Baselines(
            "data/x",
            BaselineEntry.ForEdge("T:App.Web.OldController", "T:App.Data.Db")
                .WithSiteCount(2));

        RuleResult result = Checker.Run(TwoSiteController, index, NoDataAccess)
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveGrandfathered(1);
        result.ShouldHaveGrown(0);
        result.ShrunkBaselineEntries.ShouldBe(0);
        result.UncountedBaselineEntries.ShouldBe(0);
    }

    [Fact]
    public void Check_FewerSitesThanTheEntryRecords_PassesAndCountsShrunk()
    {
        // A reduction is never red — tightening is the direction the ratchet wants — but it is reported, so
        // 'baseline --accept-reductions' has something to lower the recorded count to.
        BaselineIndex index = Checker.Baselines(
            "data/x",
            BaselineEntry.ForEdge("T:App.Web.OldController", "T:App.Data.Db")
                .WithSiteCount(3));

        RuleResult result = Checker.Run(OneController, index, NoDataAccess)
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveGrandfathered(1);
        result.ShouldHaveGrown(0);
        result.ShrunkBaselineEntries.ShouldBe(1);
        result.StaleBaselineEntries.ShouldBe(0);
    }

    [Fact]
    public void Check_UncountedEntry_GrandfathersAtPairGrainAndCountsUncounted()
    {
        // An entry recording no count holds its pair at any size, exactly as every entry did before the
        // measure existed. That is what keeps a partially upgraded or foreign baseline section valid — and
        // it is reported, because it is the state a write can clear.
        BaselineIndex index = Checker.Baselines(
            "data/x", BaselineEntry.ForEdge("T:App.Web.OldController", "T:App.Data.Db"));

        RuleResult result = Checker.Run(TwoSiteController, index, NoDataAccess)
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveGrandfathered(1);
        result.ShouldHaveGrown(0);
        result.UncountedBaselineEntries.ShouldBe(1);
    }

    [Fact]
    public void Check_SubjectEntry_IsNeitherGrownNorUncounted()
    {
        // A subject entry's sites are declarations, so there is no measure to record and nothing to report:
        // counting it as uncounted would leave a naming rule's whole section nagging forever with nothing
        // an author could do about it.
        const string source = "namespace App { public class GoodHandler {} public class BadThing {} }";
        BaselineIndex index = Checker.Baselines("naming/x", BaselineEntry.ForSubject("T:App.BadThing"));

        RuleResult result = Checker.Run(source, index, arch =>
                arch.Rule("naming/x")
                    .Migrate("Types are inconsistently named.", arch.Namespace("App.*").MustHaveSuffix("Handler"))
                    .Because("Handler discovery is convention-based."))
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveGrandfathered(1);
        result.ShouldHaveGrown(0);
        result.UncountedBaselineEntries.ShouldBe(0);
    }

    [Fact]
    public void Check_GrownPair_IsNotStaleAndCarriesItsStoredEntry()
    {
        // Matching is by identity, and a grown pair matched — so it is live debt that grew, never debt that
        // was fixed. Reading it as stale would offer it to 'baseline --accept-reductions', which would then
        // delete the entry recording the very debt that just got worse.
        BaselineIndex index = Checker.Baselines(
            "data/x",
            BaselineEntry.ForEdge("T:App.Web.OldController", "T:App.Data.Db")
                .WithSiteCount(1)
                .WithBecause("INC-1234"));

        RuleResult result = Checker.Run(TwoSiteController, index, NoDataAccess)
            .Single();

        result.ShouldHaveFailed();
        result.StaleBaselineEntries.ShouldBe(0);
        Violation grown = result.Violations.ShouldHaveSingleItem();
        BaselineEntry stored = result.GrownEntries[grown];
        stored.SiteCount.ShouldBe(1);
        stored.Because.ShouldBe("INC-1234");
    }

    [Fact]
    public void Check_SameEdgeUnderANarrowerCatchVerb_MeasuresOnlyThatVerbsSites()
    {
        // All three catch verbs key the identical edge (GRAMMAR §4.3), but each reports its own sites — so
        // the count is as measured by the verb that recorded it. An entry recorded under
        // MustNotCatchUnfiltered holds under that verb and reds under MustNotCatch, whose evidence is wider.
        // Inherent to counting evidence, and the remedy is 'baseline --add' re-recording the entry.
        BaselineIndex index = Checker.Baselines(
            "ex/x",
            BaselineEntry.ForEdge("T:App.Handler", "T:Errors.DbError")
                .WithSiteCount(1));

        RuleResult unfiltered = Checker.Run(ThreeCatchHandler, index, arch =>
                arch.Rule("ex/x")
                    .Migrate("Handlers catch the domain error blind.",
                        arch.Namespace("App.*").MustNotCatchUnfiltered(arch.Namespace("Errors.*")))
                    .Because("A blind catch hides a failing dependency."))
            .Single();
        RuleResult broad = Checker.Run(ThreeCatchHandler, index, arch =>
                arch.Rule("ex/x")
                    .Migrate("Handlers catch the domain error at all.",
                        arch.Namespace("App.*").MustNotCatch(arch.Namespace("Errors.*")))
                    .Because("A blind catch hides a failing dependency."))
            .Single();

        unfiltered.ShouldHavePassed();
        unfiltered.ShouldHaveGrandfathered(1);
        unfiltered.ShouldHaveGrown(0);

        broad.ShouldHaveFailedWithEdges(ViolationKind.Catch, ["App.Handler -> Errors.DbError"]);
        broad.ShouldHaveGrown(1);
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
        result.Violations.ShouldHaveSingleItem();
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
                        arch.Namespace("App.Nowhere.*").WithSuffix("Controller")
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
                        arch.Namespace("App.Nowhere.*").WithSuffix("Controller")
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
                        arch.Namespace("App.Web.*").WithSuffix("Controller")
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
                    .Enforce(arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Data.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailed();
        result.Grandfathered.ShouldBeEmpty();
    }
}
