using Shouldly;
using Xunit;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Tests.Checking.Targets;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     Checker semantics not tied to one verb: empty-subject failure, the inert-target warning scope
///     (decision 3), RuleError paths, posture skipping (Migrate/Freeze), the Freeze containment
///     formula evaluated as an Enforce rule, and deterministic violation ordering.
/// </summary>
public sealed class CheckerSemanticsTests
{
    [Fact]
    public void EmptySubject_FailsWithPinnedMessage()
    {
        RuleResult result = Checker.Run(Sources.Layered, arch =>
                arch.Rule("empty/x")
                    .Enforce(arch.Namespace("Nope.Nowhere.*").MustHaveSuffix("X"))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        Violation violation = result.Violations.Single();
        violation.Kind.ShouldBe(ViolationKind.EmptySubject);
        violation.Detail.ShouldBe("The subject selection matched no solution-declared types.");
    }

    [Fact]
    public void InertTarget_EmptyPatternTarget_WarnsButPasses()
    {
        RuleResult result = Checker.Run(Sources.Layered, arch =>
                arch.Rule("inert/x")
                    .Enforce(arch.Namespace("App.Domain.*").MustNotReference(arch.Namespace("App.Ghost.*")))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Passed);
        result.Warnings.Single().Kind.ShouldBe(CheckWarningKind.InertTarget);
    }

    [Fact]
    public void InertTarget_AbsentTypeofTarget_IsSilentWinCondition()
    {
        RuleResult result = Checker.Run(Sources.Layered, arch =>
                arch.Rule("no-guid/x")
                    .Enforce(arch.Namespace("App.Domain.*").MustNotReference(typeof(Guid)))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Passed);
        result.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void MustOnly_EmptyAllowSet_IsLoudNotInert()
    {
        RuleResult result = Checker.Run(Sources.Layered, arch =>
                arch.Rule("only/x")
                    .Enforce(arch.Namespace("App.Domain.*").MustOnlyReference(arch.Namespace("App.Ghost.*")))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void ClosedGenericTypeNounTarget_IsRuleError()
    {
        RuleResult result = Checker.Run(Sources.Hierarchy, arch =>
                arch.Rule("gen/x")
                    .Enforce(arch.Types.MustNotReference(typeof(IHandler<Order>)))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        Violation violation = result.Violations.Single();
        violation.Kind.ShouldBe(ViolationKind.RuleError);
        violation.Detail!.ShouldContain("closed generic construction");
    }

    [Fact]
    public void ClosedGenericTypeNounSubject_IsRuleError()
    {
        RuleResult result = Checker.Run(Sources.Hierarchy, arch =>
                arch.Rule("gen/x")
                    .Enforce(arch.Type(typeof(IHandler<Order>)).MustHaveSuffix("X"))
                    .Because("b"))
            .Single();

        result.Violations.Single().Kind.ShouldBe(ViolationKind.RuleError);
    }

    [Fact]
    public void UnrepresentableType_IsRuleError()
    {
        RuleResult result = Checker.Run(Sources.Layered, arch =>
                arch.Rule("ptr/x")
                    .Enforce(arch.Namespace("App.Domain.*").MustNotReference(typeof(int*)))
                    .Because("b"))
            .Single();

        result.Violations.Single().Kind.ShouldBe(ViolationKind.RuleError);
    }

    [Fact]
    public void ThrowingPredicate_IsRuleErrorNotCrash()
    {
        RuleResult result = Checker.Run(Sources.Layered, arch =>
                arch.Rule("throw/x")
                    .Enforce(arch.Types.Must(_ => throw new InvalidOperationException("boom"), "explode"))
                    .Because("b"))
            .Single();

        Violation violation = result.Violations.Single();
        violation.Kind.ShouldBe(ViolationKind.RuleError);
        violation.Detail!.ShouldContain("predicate threw");
    }

    [Fact]
    public void ContainmentFormula_AsEnforceRule_RedForInteriorReferenceGreenForFacade()
    {
        // The Freeze desugaring shape (sel.Except(facade).MustOnlyBeReferencedBy(sel ∪ facade)),
        // hand-built as Enforce so the containment logic is exercised while Freeze itself skips.
        RuleResult result = Checker.Run(Sources.Containment, arch =>
                arch.Rule("contain/x")
                    .Enforce(arch.Namespace("App.Legacy.*").Except(arch.Types.WithSuffix("Facade"))
                        .MustOnlyBeReferencedBy(arch.Namespace("App.Legacy.*")))
                    .Because("b"))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.ReferencePairs().ShouldContain("App.Client.User -> App.Legacy.Internal");
        result.ReferencePairs().ShouldNotContain("App.Client.User -> App.Legacy.IFacade");
    }

    [Fact]
    public void ViolationOrder_IsOrdinalBySourceThenTarget()
    {
        RuleResult result = Checker.Run(Sources.Layered, arch =>
                arch.Rule("order/x")
                    .Enforce(arch.Namespace("App.Domain.*").MustNotReference(arch.Namespace("App.Web.*")))
                    .Because("b"))
            .Single();

        result.ReferencePairs().ShouldBe([
            "App.Domain.Apple -> App.Web.Controller",
            "App.Domain.Service -> App.Web.Controller",
            "App.Domain.Service -> App.Web.Helper",
            "App.Domain.Zebra -> App.Web.Controller"
        ]);
    }
}