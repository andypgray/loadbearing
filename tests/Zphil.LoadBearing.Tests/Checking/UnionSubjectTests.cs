using Shouldly;
using Xunit;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     A union in subject position over the real MyApp fixture (GRAMMAR §5.1, §9): the union widens the
///     subject to the set-union of its operands, its own adjectives apply to that union rather than
///     through it ((a ∪ b) − c), and every operand must match at least one type so a typo'd operand is
///     never masked by its siblings.
/// </summary>
public sealed class UnionSubjectTests(WorkspaceFixture fixture)
{
    [Fact]
    public void UnionSubject_OperandWithNoViolation_Passes()
    {
        // Billing alone reaches nothing in Web — the green half of the pair below.
        Checker.Run(fixture.Model, arch =>
                arch.Rule("layering/union")
                    .Enforce(arch.AnyOf(arch.Project("MyApp.Legacy.Billing"))
                        .MustNotReference(arch.Namespace("MyApp.Web.*")))
                    .Because("b"))
            .Single().ShouldHavePassed();
    }

    [Fact]
    public void UnionSubject_WidensToTheSetUnionOfItsOperands()
    {
        // Adding Domain to the same union brings OrderService's reach into Web with it: the union names
        // every type either operand names, so the rule that passed over Billing alone now fails.
        RuleResult result = Checker.Run(fixture.Model, arch =>
                arch.Rule("layering/union")
                    .Enforce(arch.AnyOf(arch.Project("MyApp.Legacy.Billing"), arch.Project("MyApp.Domain"))
                        .MustNotReference(arch.Namespace("MyApp.Web.*")))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.ReferencePairs().ShouldContain("MyApp.Domain.OrderService -> MyApp.Web.HomeController");
    }

    [Fact]
    public void UnionExcept_SubtractsFromTheUnionNotFromEachOperand()
    {
        // (a ∪ b) − c: excluding the one Domain type that reaches Web makes the union rule green again,
        // which is only true if the Except applies after the set union.
        Checker.Run(fixture.Model, arch =>
                arch.Rule("layering/union")
                    .Enforce(arch.AnyOf(arch.Project("MyApp.Legacy.Billing"), arch.Project("MyApp.Domain"))
                        .Except(arch.Types.WithNameMatching("OrderService"))
                        .MustNotReference(arch.Namespace("MyApp.Web.*")))
                    .Because("b"))
            .Single().ShouldHavePassed();
    }

    [Fact]
    public void UnionAdjective_NarrowsTheUnionedSet()
    {
        // A union-level OfKind applies over the union: the I-prefix law holds across both projects' interfaces.
        Checker.Run(fixture.Model, arch =>
                arch.Rule("naming/union-interfaces")
                    .Enforce(arch.AnyOf(arch.Project("MyApp.Web"), arch.Project("MyApp.Legacy.Billing"))
                        .OfKind(TypeKind.Interface)
                        .MustHavePrefix("I"))
                    .Because("b"))
            .Single().ShouldHavePassed();
    }

    [Fact]
    public void EmptyOperand_FailsTheRuleNamingThatOperand()
    {
        // §9, law must load predictably: a typo'd project name inside a union fails loudly rather than
        // quietly contributing nothing while its siblings carry the rule.
        RuleResult result = Checker.Run(fixture.Model, arch =>
                arch.Rule("naming/union")
                    .Enforce(arch.AnyOf(arch.Project("MyApp.Domain"), arch.Project("MyApp.Domian"))
                        .MustHaveNameMatching("*"))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithDetail(
            ViolationKind.EmptySubject,
            "The subject selection operand \"types in project `MyApp.Domian`\" matched no solution-declared types.");
    }

    [Fact]
    public void EmptyOperand_ReportsOnePerEmptyOperandInOperandOrder()
    {
        RuleResult result = Checker.Run(fixture.Model, arch =>
                arch.Rule("naming/union")
                    .Enforce(arch.AnyOf(arch.Project("MyApp.Nope"), arch.Project("MyApp.Domain"), arch.Namespace("MyApp.Nope.*"))
                        .MustHaveNameMatching("*"))
                    .Because("b"))
            .Single();

        result.Violations.Select(v => v.Detail).ShouldBe(
        [
            "The subject selection operand \"types in project `MyApp.Nope`\" matched no solution-declared types.",
            "The subject selection operand \"types in `MyApp.Nope.*`\" matched no solution-declared types."
        ]);
    }

    [Fact]
    public void EmptyOperand_UnderAMemberSubject_FailsThroughTheSameGate()
    {
        // MemberConstraint.Subject IS the underlying type selection, so the one gate covers the member path —
        // which is the path the repo's own flagship union rule takes.
        RuleResult result = Checker.Run(fixture.Model, arch =>
                arch.Rule("naming/union-members")
                    .Enforce(arch.AnyOf(arch.Project("MyApp.Domain"), arch.Project("MyApp.Domian"))
                        .Methods.MustHaveNameMatching("*"))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithDetail(
            ViolationKind.EmptySubject,
            "The subject selection operand \"types in project `MyApp.Domian`\" matched no solution-declared types.");
    }

    [Fact]
    public void UnionMemberSubject_ResolvesMembersAcrossEveryOperand()
    {
        // The dogfood shape end to end: a member projection over a project union resolves and checks.
        Checker.Run(fixture.Model, arch =>
                arch.Rule("naming/union-members")
                    .Enforce(arch.AnyOf(arch.Project("MyApp.Domain"), arch.Project("MyApp.Legacy.Billing"))
                        .Methods.MustHaveNameMatching("*"))
                    .Because("b"))
            .Single().ShouldHavePassed();
    }
}