using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Tests.Checking.Targets;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     The narrowed-universe skip: under a solution filter, a rule whose <em>every</em> violation is an
///     empty subject reached no verdict rather than a red one — its subject lived in the projects the
///     filter left out, so the run has nothing to say about it. All three empty-selection sites take it
///     (type subject, member subject, per-operand union), and nothing else does.
/// </summary>
/// <remarks>
///     The two guards either side of the predicate are the point. With no narrowing an empty subject is
///     still red, because a spec naming a namespace the solution does not have is a defect and the whole
///     of GRAMMAR §4.1 rests on that; and under a narrowing a rule with a real finding is still red,
///     because types did load and the rule was measured. The wording of the reason is not pinned here — it
///     is minted in <c>NarrowedUniverseNotice.RuleSkipReason</c> and pinned there and end to end in
///     <see cref="Zphil.LoadBearing.Tests.Cli.FilteredSolutionE2ETests" />; what these rows pin is which
///     rules carry it.
/// </remarks>
public sealed class NarrowedUniverseSkipTests
{
    // One controller reaching the data layer: a real violation available to any rule that asks for it, and
    // two namespaces that exist, so an empty subject here is always the selection's own doing.
    private const string WebAndData = """
                                      namespace App.Web { public class HomeController { public App.Data.Db Load() => new App.Data.Db(); } }
                                      namespace App.Data { public class Db {} }
                                      """;

    // A stand-in for the composed reason, deliberately not the real wording: these rows are about which
    // rules take it, and pinning the sentence twice would make one of the two pins the wrong place to look.
    private const string SkipReason = "'Leaf.slnf' narrowed this run: 2 projects were not checked.";

    [Fact]
    public void Check_TypeSubjectMatchedNothingUnderNarrowing_Skips()
    {
        // The commonest shape by far: a whole layer's rule whose namespace lives in a project the filter
        // dropped. Red, it reads as "your spec names a namespace that does not exist" — for a namespace the
        // solution does have.
        RuleResult result = Checker.Run(WebAndData, BaselineIndex.Empty, Narrowed(), arch =>
                arch.Rule("layering/x")
                    .Enforce(arch.Namespace("App.Nowhere.*").MustNotReference(arch.Namespace("App.Data.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveSkipped(SkipReason);
    }

    [Fact]
    public void Check_MemberSubjectMatchedNothingUnderNarrowing_Skips()
    {
        // The member gate dispatches before the type-subject one and speaks in member terms (GRAMMAR §4.6),
        // so it is its own empty-selection site and would otherwise red on its own message.
        RuleResult result = Checker.Run(WebAndData, BaselineIndex.Empty, Narrowed(), arch =>
                arch.Rule("member/x")
                    .Enforce(arch.Namespace("App.Nowhere.*").Methods.MustBePublic())
                    .Because("b"))
            .Single();

        result.ShouldHaveSkipped(SkipReason);
    }

    [Fact]
    public void Check_UnionOperandMatchedNothingUnderNarrowing_Skips()
    {
        // The third site: the per-operand gate (GRAMMAR §9) fires for an operand that matched nothing even
        // when its siblings matched plenty, and returns only those. Under a filter a dropped project is
        // exactly what an empty operand looks like — which is why the run declines rather than accuses.
        RuleResult result = Checker.Run(WebAndData, BaselineIndex.Empty, Narrowed(), arch =>
                arch.Rule("layering/x")
                    .Enforce(arch.AnyOf(arch.Namespace("App.Web.*"), arch.Namespace("App.Nowhere.*"))
                        .MustNotReference(arch.Namespace("App.Data.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveSkipped(SkipReason);
    }

    [Fact]
    public void Check_EmptySubjectWithNoNarrowing_IsStillRed()
    {
        // The invariant this whole change is measured against: an unfiltered run must reach byte-identical
        // verdicts. Over the whole solution an empty subject is a spec defect and stays loud.
        RuleResult result = Checker.Run(WebAndData, BaselineIndex.Empty, null, arch =>
                arch.Rule("layering/x")
                    .Enforce(arch.Namespace("App.Nowhere.*").MustNotReference(arch.Namespace("App.Data.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithDetail(ViolationKind.EmptySubject, ConstraintEvaluator.EmptySubjectMessage);
    }

    [Fact]
    public void Check_RealViolationUnderNarrowing_IsStillRed()
    {
        // The predicate is "every violation is an empty subject", not "the run was narrowed". A finding over
        // types that did load is a finding, and a filter is no reason to hold it back.
        RuleResult result = Checker.Run(WebAndData, BaselineIndex.Empty, Narrowed(), arch =>
                arch.Rule("layering/x")
                    .Enforce(arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Data.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithSingleEdge(ViolationKind.Reference, "App.Web.HomeController", "App.Data.Db");
    }

    [Fact]
    public void Check_RuleErrorUnderNarrowing_IsStillRed()
    {
        // The other never-baselinable violation, and the one kind deliberately outside the skip: a rule that
        // could not be evaluated is broken whatever the universe, and a filter must not quieten it.
        RuleResult result = Checker.Run(Sources.HierarchyModel, BaselineIndex.Empty, Narrowed(), arch =>
                arch.Rule("layering/x")
                    .Enforce(arch.Types.MustNotReference(typeof(IHandler<Order>)))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailed();
        result.Violations.ShouldHaveSingleItem()
            .Kind.ShouldBe(ViolationKind.RuleError);
    }

    // The narrowing a filtered run hands the checker: the one line a rule it emptied reports.
    private static NarrowedUniverse Narrowed()
    {
        return new NarrowedUniverse(SkipReason);
    }
}
