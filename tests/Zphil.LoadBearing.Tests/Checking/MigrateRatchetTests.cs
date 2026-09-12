using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Tests.Checking.Targets;
using Zphil.LoadBearing.Tests.Extraction;
using Zphil.LoadBearing.Tests.TestSupport;

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

    // One misnamed type beside one conforming one — the shape the subject-keyed ratchet rows read.
    private const string MixedNames = "namespace App { public class GoodHandler {} public class BadThing {} }";

    // A stand-in for the reason a filtered run composes; its wording is pinned where it is minted.
    private const string NarrowingSkipReason = "'BillingOnly.slnf' narrowed this run: 2 projects were not checked.";

    // The one forbidden edge every controller fixture carries, as the baseline keys it.
    private static readonly BaselineEntry OldControllerToDb = BaselineEntry.ForEdge("T:App.Web.OldController", "T:App.Data.Db");

    private static readonly CodebaseModel ThreeCatchHandlerModel = CompilationFactory.Extract(ThreeCatchHandler);

    // The one forbidden edge again, this time as a model carrying it with no site at all — the state a
    // legacy file's uncounted entry describes, arrived at from the run's end instead of the file's.
    private static readonly CodebaseModel UnsitedControllerEdge = UnsitedEdge();

    // The same source's rule: every type in App must carry the Handler suffix.
    private static void HandlerNaming(Arch arch)
    {
        arch.Rule("naming/x")
            .Migrate("Types are inconsistently named.", arch.Namespace("App.*").MustHaveSuffix("Handler"))
            .Because("Handler discovery is convention-based.");
    }

    // The same forbidden reference the controller fixtures carry, selected by suffix rather than by
    // namespace: a hand-built node declares no namespace, so only a name-keyed selection reaches one.
    private static void NoDbFromControllers(Arch arch)
    {
        arch.Rule("data/x")
            .Migrate("old", arch.Types.WithSuffix("Controller").MustNotReference(arch.Types.WithSuffix("Db")))
            .Because("b");
    }

    private static CodebaseModel UnsitedEdge()
    {
        TypeNode source = SyntheticNodes.Type("App.Web.OldController");
        TypeNode target = SyntheticNodes.Type("App.Data.Db");

        return SyntheticNodes.Referencing([source, target], new ReferenceEdge(source, target, []));
    }

    // The empty-subject spec both no-match rows run: a controller cone no type occupies.
    private static void NoDataAccessFromNowhere(Arch arch)
    {
        arch.Rule("data/x")
            .Migrate(
                "old",
                arch.Namespace("App.Nowhere.*").WithSuffix("Controller")
                    .MustNotReference(arch.Namespace("App.Data.*")))
            .Because("b");
    }

    [Fact]
    public void Check_MigrateViolationInBaseline_PassesWithGrandfathered()
    {
        BaselineIndex index = Checker.Baselines("data/x", OldControllerToDb);

        RuleResult result = Checker.Run(Sources.OneControllerModel, index, Sources.NoDataAccess)
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
        RuleResult result = Checker.Run(Sources.OneControllerModel, Checker.Baselines("data/x"), Sources.NoDataAccess)
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
        BaselineIndex index = Checker.Baselines("data/x", OldControllerToDb);

        RuleResult result = Checker.Run(source, index, Sources.NoDataAccess)
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
        BaselineIndex index = Checker.Baselines("data/x", OldControllerToDb);

        RuleResult result = Checker.Run(Sources.OneControllerModel, index, arch =>
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
        BaselineIndex index = Checker.Baselines("naming/x", BaselineEntry.ForSubject("T:App.BadThing"));

        RuleResult result = Checker.Run(MixedNames, index, HandlerNaming)
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveGrandfathered(1);
        result.Violations.ShouldBeEmpty();
    }

    [Fact]
    public void Check_MoreSitesThanTheEntryRecords_IsRedAsGrowth()
    {
        // The hole the measure closes: without a recorded count, a second old-pattern site inside an
        // already-grandfathered pair rides in free — which is precisely where new code in the old pattern
        // gets written, inside a type whose surrounding code already does it.
        BaselineIndex index = Checker.Baselines(
            "data/x",
            OldControllerToDb.WithSiteCount(1));

        RuleResult result = Checker.Run(Sources.TwoSiteControllerModel, index, Sources.NoDataAccess)
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
            OldControllerToDb.WithSiteCount(2));

        RuleResult result = Checker.Run(Sources.TwoSiteControllerModel, index, Sources.NoDataAccess)
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveGrandfathered(1);
        result.ShouldHaveGrown(0);
        result.ShouldHaveShrunk(0);
        result.ShouldHaveUncounted(0);
    }

    [Fact]
    public void Check_FewerSitesThanTheEntryRecords_PassesAndCountsShrunk()
    {
        // A reduction is never red — tightening is the direction the ratchet wants — but it is reported, so
        // 'baseline --accept-reductions' has something to lower the recorded count to.
        BaselineIndex index = Checker.Baselines(
            "data/x",
            OldControllerToDb.WithSiteCount(3));

        RuleResult result = Checker.Run(Sources.OneControllerModel, index, Sources.NoDataAccess)
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveGrandfathered(1);
        result.ShouldHaveGrown(0);
        result.ShouldHaveShrunk(1);
        result.ShouldHaveStale(0);
    }

    [Fact]
    public void Check_UncountedEntry_GrandfathersAtPairGrainAndCountsUncounted()
    {
        // An entry recording no count holds its pair at any size. That is what keeps a partially upgraded
        // or foreign baseline section valid — and it is reported, because it is the state a write can clear.
        BaselineIndex index = Checker.Baselines(
            "data/x", OldControllerToDb);

        RuleResult result = Checker.Run(Sources.TwoSiteControllerModel, index, Sources.NoDataAccess)
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveGrandfathered(1);
        result.ShouldHaveGrown(0);
        result.ShouldHaveUncounted(1);
    }

    [Fact]
    public void Check_EdgeSeenWithNoSites_IsGrandfatheredAndCountsNeitherShrunkNorUncounted()
    {
        // An edge the run saw with no file:line evidence is the same state as an entry written before counts
        // existed: grandfathered at pair grain, with nothing to measure and nothing to report. Read as a
        // reduction instead, it would offer 'baseline --accept-reductions' a count of zero to record, which
        // an entry may not carry. The model is hand-built because no extraction reaches this state — every
        // edge factory is fed from syntax — and the verdict path has to agree with the write path anyway.
        BaselineIndex index = Checker.Baselines("data/x", OldControllerToDb.WithSiteCount(2));

        RuleResult result = Checker.Run(UnsitedControllerEdge, index, NoDbFromControllers)
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveGrandfathered(1);
        result.ShouldHaveGrown(0);
        result.ShouldHaveShrunk(0);
        result.ShouldHaveUncounted(0);
    }

    [Fact]
    public void Check_SubjectEntry_IsNeitherGrownNorUncounted()
    {
        // A subject entry's sites are declarations, so there is no measure to record and nothing to report:
        // counting it as uncounted would leave a naming rule's whole section nagging forever with nothing
        // an author could do about it.
        BaselineIndex index = Checker.Baselines("naming/x", BaselineEntry.ForSubject("T:App.BadThing"));

        RuleResult result = Checker.Run(MixedNames, index, HandlerNaming)
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveGrandfathered(1);
        result.ShouldHaveGrown(0);
        result.ShouldHaveUncounted(0);
    }

    [Fact]
    public void Check_GrownPair_IsNotStaleAndCarriesItsStoredEntry()
    {
        // Matching is by identity, and a grown pair matched — so it is live debt that grew, never debt that
        // was fixed. Reading it as stale would offer it to 'baseline --accept-reductions', which would then
        // delete the entry recording the very debt that just got worse.
        BaselineIndex index = Checker.Baselines(
            "data/x",
            OldControllerToDb
                .WithSiteCount(1)
                .WithBecause("INC-1234"));

        RuleResult result = Checker.Run(Sources.TwoSiteControllerModel, index, Sources.NoDataAccess)
            .Single();

        result.ShouldHaveFailed();
        result.ShouldHaveStale(0);
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

        RuleResult unfiltered = Checker.Run(ThreeCatchHandlerModel, index, arch =>
                arch.Rule("ex/x")
                    .Migrate("Handlers catch the domain error blind.",
                        arch.Namespace("App.*").MustNotCatchUnfiltered(arch.Namespace("Errors.*")))
                    .Because("A blind catch hides a failing dependency."))
            .Single();
        RuleResult broad = Checker.Run(ThreeCatchHandlerModel, index, arch =>
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
            OldControllerToDb,
            BaselineEntry.ForEdge("T:App.Web.GhostController", "T:App.Data.Db"));

        RuleResult result = Checker.Run(Sources.OneControllerModel, index, Sources.NoDataAccess)
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveGrandfathered(1);
        result.ShouldHaveStale(1);
    }

    [Fact]
    public void Check_UncapturedMigrateRule_AllViolationsRedAndCapturedFalse()
    {
        RuleResult result = Checker.Run(Sources.OneControllerModel, BaselineIndex.Empty, Sources.NoDataAccess)
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

        RuleResult result = Checker.Run(source, Checker.Baselines("data/x"), Sources.NoDataAccess)
            .Single();

        result.ShouldHavePassed();
        result.BaselineCaptured.ShouldBeTrue();
        result.Grandfathered.ShouldBeEmpty();
        result.ShouldHaveStale(0);
    }

    [Fact]
    public void Check_MigrateEmptySubject_IsRedNeverBaselinable()
    {
        RuleResult result = Checker.Run(
                "namespace App { public class X {} }", Checker.Baselines("data/x"), NoDataAccessFromNowhere)
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
            OldControllerToDb,
            BaselineEntry.ForEdge("T:App.Web.GhostController", "T:App.Data.Db"));
        var narrowing = new NarrowedUniverse(NarrowingSkipReason);

        RuleResult result = Checker.Run(
                "namespace App { public class X {} }", index, narrowing, NoDataAccessFromNowhere)
            .Single();

        result.ShouldHaveSkipped(NarrowingSkipReason);
        result.ShouldHaveGrandfathered(0);
        result.ShouldHaveStale(0);
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
    public void Check_MigrateInertTargetOverACapturedSection_MarksTheRuleAsHavingMeasuredNothing()
    {
        // The arm the warns-and-passes row above cannot reach: its baseline is empty, so the whole captured
        // section going unmatched never happens there. Here two entries are captured and the target has rotted,
        // which leaves the rule passing with nothing tolerated, nothing red, and its entire section stale —
        // the four counts a burned-down migration has, on a rule that measured nothing. The predicate is what
        // parts them, and every surface downstream (the status term, the roll-up, the baseline refusal,
        // Promotable) reads it rather than re-deriving it.
        BaselineIndex index = Checker.Baselines(
            "data/x",
            OldControllerToDb,
            BaselineEntry.ForEdge("T:App.Web.GhostController", "T:App.Data.Db"));

        RuleResult result = Checker.Run("namespace App.Web { public class HomeController {} }", index, arch =>
                arch.Rule("data/x")
                    .Migrate("old", arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Ghost.*")))
                    .Baseline("ghost.json")
                    .Because("b"))
            .Single();

        result.ShouldHaveWarnedInertTarget();
        result.ShouldHaveGrandfathered(0);
        result.ShouldHaveStale(2);
        result.SelectionMatchedNothing.ShouldBeTrue();
        result.Promotable.ShouldBeFalse("a rule that measured nothing must never be offered for promotion");
    }

    [Fact]
    public void Check_MigrateEmptySubject_MarksTheRuleAsHavingMeasuredNothingToo()
    {
        // The same fact arriving as a failure rather than a warning: an empty subject is one violation no
        // baseline can hold, so the rule reds and its whole section still goes unmatched. One predicate
        // answers for both, because a reader testing only the violations would miss the inert half and a
        // reader testing only the warnings would miss this one.
        BaselineIndex index = Checker.Baselines("data/x", OldControllerToDb);

        RuleResult result = Checker.Run("namespace App.Data { public class Db {} }", index, arch =>
                arch.Rule("data/x")
                    .Migrate("old", arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Data.*")))
                    .Baseline("ghost.json")
                    .Because("b"))
            .Single();

        result.ShouldHaveFailed();
        result.Violations.ShouldHaveSingleItem()
            .Kind.ShouldBe(ViolationKind.EmptySubject);
        result.ShouldHaveStale(1);
        result.SelectionMatchedNothing.ShouldBeTrue();
    }

    [Fact]
    public void Check_MigrateThatMeasuredItsSubject_DoesNotReadAsHavingMeasuredNothing()
    {
        // The other side of the predicate, so it cannot pass by answering true always: a rule with a real
        // subject and a real target reds on a real finding and nothing about its selection came up empty.
        RuleResult result = Checker.Run(Sources.OneControllerModel, BaselineIndex.Empty, Sources.NoDataAccess)
            .Single();

        result.ShouldHaveFailed();
        result.SelectionMatchedNothing.ShouldBeFalse();
    }

    [Fact]
    public void Check_TwoArgOverload_TreatsBaselinesAsEmpty()
    {
        RuleResult result = Checker.Run(Sources.OneControllerModel, Sources.NoDataAccess)
            .Single();

        result.ShouldHaveFailed();
        result.BaselineCaptured.ShouldBeFalse();
    }

    [Fact]
    public void Check_EnforceRule_IgnoresBaselineIndex()
    {
        // Even an index that carries the exact edge does not grandfather an Enforce rule.
        BaselineIndex index = Checker.Baselines("layer/x", OldControllerToDb);

        RuleResult result = Checker.Run(Sources.OneControllerModel, index, arch =>
                arch.Rule("layer/x")
                    .Enforce(arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Data.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailed();
        result.Grandfathered.ShouldBeEmpty();
    }
}
