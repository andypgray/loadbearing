using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     The coverage verb <c>MustBelongTo</c> over the fast path (GRAMMAR §5.3, §4.1, §4.3): a subject no
///     membership names is red, so the rule closes the ungoverned remainder a set of positive rules leaves
///     open. Memberships resolve in <em>subject</em> position — they name where a type may live rather than
///     the far end of an edge — and the test is plain membership in their union.
/// </summary>
/// <remarks>
///     The verb reads the selection membership the model already resolves, so it adds no extracted fact, no
///     cache-schema change and nothing for the extraction tests to cover. Violations are
///     <see cref="ViolationKind.Shape" />: identity is the subject symbol ID riding
///     <see cref="BaselineEntry.ForSubject" />, which leaves the membership list part of the rule and never
///     part of the identity — a rule that gains a membership keeps its blessed entries. A membership that
///     matches nothing is silent, because operand emptiness is not subject emptiness and the shape family
///     raises no inert-target warning.
/// </remarks>
public sealed class MustBelongToVerbTests
{
    // Two declared layers and one namespace outside both. Legacy.Ledger is the ungoverned remainder every
    // red below is about; Domain carries two types so a narrowed membership has something to drop.
    private const string Scene = """
                                 namespace Domain
                                 {
                                     public class Order {}
                                     public class Invoice {}
                                 }
                                 namespace Web
                                 {
                                     public class HomeController {}
                                 }
                                 namespace Legacy
                                 {
                                     public class Ledger {}
                                 }
                                 """;

    private static readonly CodebaseModel SceneModel = CompilationFactory.Extract(Scene);

    [Fact]
    public void MustBelongTo_SubjectInNoMembership_FailsWithSubjectAndDeclarationSites()
    {
        // A shape violation has no edge to cite, so the file:line an agent jumps to is where the uncovered
        // type is declared — the subject's own declaration sites, carried as evidence.
        Checker.Run(SceneModel, arch =>
                arch.Rule("layering/no-ungoverned-types")
                    .Enforce(arch.Types.MustBelongTo(arch.Namespace("Domain.*"), arch.Namespace("Web.*")))
                    .Because("b"))
            .Single()
            .ShouldHaveFailedWithSubjectAtSites("Legacy.Ledger", ["Test.cs:12"]);
    }

    [Fact]
    public void MustBelongTo_EverySubjectCoveredByAMembership_PassesClean()
    {
        Checker.Run(SceneModel, arch =>
                arch.Rule("layering/no-ungoverned-types")
                    .Enforce(arch.Types.Except(arch.Namespace("Legacy.*"))
                        .MustBelongTo(arch.Namespace("Domain.*"), arch.Namespace("Web.*")))
                    .Because("b"))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustBelongTo_SingleMembership_IsPlainMembershipInThatSelection()
    {
        // One membership is the degenerate case and stays a membership test rather than becoming an identity
        // test: the types the selection names pass, and everything else is the remainder.
        Checker.Run(SceneModel, arch =>
                arch.Rule("layering/domain-only")
                    .Enforce(arch.Namespace("Domain.*").MustBelongTo(arch.Namespace("Domain.*")))
                    .Because("b"))
            .Single()
            .ShouldHavePassedClean();

        Checker.Run(SceneModel, arch =>
                arch.Rule("layering/domain-only")
                    .Enforce(arch.Types.MustBelongTo(arch.Namespace("Domain.*")))
                    .Because("b"))
            .Single()
            .ShouldHaveFailedWithSubjects(["Web.HomeController", "Legacy.Ledger"]);
    }

    [Fact]
    public void MustBelongTo_UnionMembershipAndAnExceptedOne_NarrowTheResolvedSet()
    {
        // A union operand resolves as ONE membership covering both its parts, so this names the same set the
        // two-membership spelling above does.
        Checker.Run(SceneModel, arch =>
                arch.Rule("layering/union-membership")
                    .Enforce(arch.Types.MustBelongTo(arch.AnyOf(arch.Namespace("Domain.*"), arch.Namespace("Web.*"))))
                    .Because("b"))
            .Single()
            .ShouldHaveFailedWithSubjects(["Legacy.Ledger"]);

        // Except narrows the membership itself: Domain minus Invoice no longer covers Invoice, so the type
        // joins the uncovered remainder rather than staying green on a layer it is still inside.
        Checker.Run(SceneModel, arch =>
                arch.Rule("layering/excepted-membership")
                    .Enforce(arch.Types.MustBelongTo(
                        arch.Namespace("Domain.*").Except(arch.Types.WithNameMatching("Invoice")),
                        arch.Namespace("Web.*")))
                    .Because("b"))
            .Single()
            .ShouldHaveFailedWithSubjects(["Domain.Invoice", "Legacy.Ledger"]);
    }

    [Fact]
    public void MustBelongTo_MembershipMatchingNothing_IsSilentAndTheRedsAreTheUncoveredSubjects()
    {
        // A membership naming nothing covers nothing, and that is the whole of its effect. The per-operand
        // emptiness failure guards a union SUBJECT (§9), and the inert-target warning belongs to the
        // forbidden-set verbs (§4.1) — neither reaches a membership, so the report says exactly which
        // subjects went uncovered and nothing else.
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("layering/absent-membership")
                    .Enforce(arch.Types.MustBelongTo(arch.Namespace("Domain.*"), arch.Namespace("Nowhere.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithSubjects(["Web.HomeController", "Legacy.Ledger"]);

        result.ShouldSatisfyAllConditions(
            () => result.Warnings.ShouldBeEmpty(),
            () => result.Violators(ViolationKind.EmptySubject, violation => violation.Detail!)
                .ShouldBeEmpty());
    }

    [Fact]
    public void MustBelongTo_EmptySubject_FailsWithDefaultMessage()
    {
        // An empty subject fails the rule by default with the shared message (GRAMMAR §4.1) — the coverage
        // verb takes the same subject gate, which is what keeps an empty membership and an empty subject
        // distinguishable outcomes.
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("layering/empty")
                    .Enforce(arch.Namespace("Nowhere.*").MustBelongTo(arch.Namespace("Domain.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithDetail(ViolationKind.EmptySubject, ConstraintEvaluator.EmptySubjectMessage);
    }

    [Fact]
    public void MustBelongTo_GrandfatheredStrayPasses_NewStrayStaysRed()
    {
        // Identity is the subject symbol ID (GRAMMAR §4.3), one entry per uncovered type. Legacy.Ledger is
        // blessed; Web.HomeController — uncovered because the rule declares only the Domain membership — is a
        // distinct identity and stays red.
        BaselineIndex index = Checker.Baselines(
            "layering/no-ungoverned-types", BaselineEntry.ForSubject("T:Legacy.Ledger"));

        RuleResult result = Checker.Run(SceneModel, index, arch =>
                arch.Rule("layering/no-ungoverned-types")
                    .Migrate(
                        "Some types predate the declared layers.",
                        arch.Types.MustBelongTo(arch.Namespace("Domain.*")))
                    .Because("A type in no declared layer is governed by nothing."))
            .Single();

        result.ShouldHaveFailedWithSubjects(["Web.HomeController"]);
        result.ShouldHaveGrandfathered(1);
    }
}
