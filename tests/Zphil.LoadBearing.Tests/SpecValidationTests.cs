using Shouldly;
using Xunit;
using Zphil.LoadBearing.Validation;
using Code = Zphil.LoadBearing.Validation.SpecValidationErrorCode;

namespace Zphil.LoadBearing.Tests;

/// <summary>
///     The spec-build validation catalog (GRAMMAR §8): one failing spec per code, pinned by code
///     plus rule ID with one representative message, and one all-errors spec proving every problem
///     is reported in a single pass.
/// </summary>
public class SpecValidationTests
{
    private static SpecValidationException BuildExpectingFailure(params IArchitectureSpec[] specs)
    {
        return Should.Throw<SpecValidationException>(() => ArchModelBuilder.Build(specs));
    }

    [Fact]
    public void DuplicateId_AcrossTwoSpecClasses_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new DuplicateIdSpecA(), new DuplicateIdSpecB());

        ex.Errors.ShouldContain(e => e.Code == Code.DuplicateId && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.DuplicateId).Message.ShouldBe("Duplicate rule ID 'area/rule'.");
    }

    [Fact]
    public void IdExtendsScope_ReservedNamespace_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new IdExtendsScopeSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.IdExtendsScope && e.RuleId == "legacy/billing/foo");
        ex.Errors.First(e => e.Code == Code.IdExtendsScope).Message.ShouldContain("extends scope 'legacy/billing'");
    }

    [Fact]
    public void DanglingAnchor_RuleWithNoPosture_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new DanglingRuleSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.DanglingAnchor && e.RuleId == "area/dangling");
    }

    [Fact]
    public void MissingBecause_OnRule_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new MissingBecauseRuleSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.MissingBecause && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.MissingBecause).Message.ShouldBe("'area/rule' is missing a required .Because(...).");
    }

    [Fact]
    public void MissingBecause_OnFrozenScope_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new MissingBecauseScopeSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.MissingBecause && e.RuleId == "legacy/billing");
    }

    [Fact]
    public void MissingDragons_OnFrozenScope_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new MissingDragonsSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.MissingDragons && e.RuleId == "legacy/billing");
    }

    [Fact]
    public void BlankProse_BlankEscapeHatchDescription_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new BlankDescriptionSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.BlankProse && e.RuleId == "area/rule");
    }

    [Fact]
    public void MultiLineProse_NewlineInBecause_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new MultiLineBecauseSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.MultiLineProse && e.RuleId == "area/rule");
    }

    [Fact]
    public void RepeatedTrailer_BecauseTwice_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new RepeatedBecauseSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.RepeatedTrailer && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.RepeatedTrailer).Message.ShouldBe("Repeated trailer 'Because' on 'area/rule'.");
    }

    [Fact]
    public void MalformedId_NonConventionId_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new MalformedIdSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.MalformedId && e.RuleId == "Bad_Id");
    }

    [Fact]
    public void EmptyBoundary_BoundaryOnlyViaWithNoTypes_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new EmptyBoundarySpec());

        ex.Errors.ShouldContain(e => e.Code == Code.EmptyBoundary && e.RuleId == "legacy/billing");
        ex.Errors.First(e => e.Code == Code.EmptyBoundary).Message.ShouldContain("omit the call for a hermetic freeze");
    }

    [Fact]
    public void DuplicateLayerName_IsReportedSpecWide()
    {
        SpecValidationException ex = BuildExpectingFailure(new DuplicateLayerSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.DuplicateLayerName && e.RuleId == null);
        ex.Errors.First(e => e.Code == Code.DuplicateLayerName).Message.ShouldBe("Duplicate layer name 'Dup'.");
    }

    [Fact]
    public void ForeignSelection_SelectionFromAnotherArch_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new ForeignSelectionSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.ForeignSelection && e.RuleId == "area/rule");
    }

    [Fact]
    public void AllErrors_AreReportedInOnePass()
    {
        SpecValidationException ex = BuildExpectingFailure(new MultipleProblemsSpec());

        ex.Errors.Select(e => e.Code).Distinct().Count().ShouldBeGreaterThanOrEqualTo(3);
        ex.Errors.ShouldContain(e => e.Code == Code.MalformedId);
        ex.Errors.ShouldContain(e => e.Code == Code.MissingBecause);
        ex.Errors.ShouldContain(e => e.Code == Code.DanglingAnchor);
    }

    [Fact]
    public void BlankMemberName_OnMustNotUse_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new BlankMemberNameSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.BlankMemberName && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.BlankMemberName).Message
            .ShouldBe("Blank member name on a member of 'System.DateTime' (used by 'area/rule').");
    }

    [Fact]
    public void MemberNotDeclared_TypoName_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new TypoMemberSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.MemberNotDeclared && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.MemberNotDeclared).Message
            .ShouldBe("'System.DateTime' does not declare a member named 'Nows' (used by 'area/rule').");
    }

    [Fact]
    public void MemberNotDeclared_MemberOnBaseType_NamesBaseAndTypeof()
    {
        SpecValidationException ex = BuildExpectingFailure(new BaseTypeMemberSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.MemberNotDeclared && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.MemberNotDeclared).Message
            .ShouldBe("'System.Threading.Tasks.Task<TResult>' does not declare 'Wait'; it is declared on base type " +
                      "'System.Threading.Tasks.Task' — use typeof(Task) (used by 'area/rule').");
    }

    [Fact]
    public void ForeignMember_MemberFromAnotherArch_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new ForeignMemberSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.ForeignMember && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.ForeignMember).Message
            .ShouldBe("A member used by 'area/rule' was minted on a different Arch instance; it is not registered with this model.");
    }

    [Fact]
    public void ValidMemberUse_MustNotUseSpec_BuildsWithoutError()
    {
        Should.NotThrow(() => ArchModelBuilder.Build(new ValidMemberUseSpec()));
    }

    [Fact]
    public void MemberReturningClosedGeneric_ClosedAnchor_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new ClosedGenericReturningSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.MemberReturningClosedGeneric && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.MemberReturningClosedGeneric).Message
            .ShouldBe("'System.Threading.Tasks.Task<System.Int32>' is a closed generic; .Returning matches definition-level — " +
                      "use typeof(Task<>) (used by 'area/rule').");
    }

    [Fact]
    public void BlankProse_BlankMemberWhereDescription_IsReported()
    {
        // The extended prose walk reaches a member Where description (GRAMMAR §8 item 5, §4.6).
        SpecValidationException ex = BuildExpectingFailure(new BlankMemberWhereSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.BlankProse && e.RuleId == "area/rule");
    }

    [Fact]
    public void BlankProse_BlankMemberMustDescription_IsReported()
    {
        // The extended prose walk reaches a member Must description (GRAMMAR §8 item 5, §4.6).
        SpecValidationException ex = BuildExpectingFailure(new BlankMemberMustSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.BlankProse && e.RuleId == "area/rule");
    }

    [Fact]
    public void ValidMemberSubject_AsyncSuffixSpec_BuildsWithoutError()
    {
        // Non-generic + open-generic Returning anchors are both accepted (only closed generics fail).
        Should.NotThrow(() => ArchModelBuilder.Build(new ValidMemberSubjectSpec()));
    }

    private sealed class DuplicateIdSpecA : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("area/rule").Enforce(arch.Types.MustHavePrefix("I")).Because("A.");
        }
    }

    private sealed class DuplicateIdSpecB : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("area/rule").Enforce(arch.Types.MustHavePrefix("I")).Because("B.");
        }
    }

    private sealed class IdExtendsScopeSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Scope("legacy/billing").Freeze(arch.Namespace("MyApp.Legacy.Billing.*"))
                .Dragons("Dragons.").Because("Frozen.");
            arch.Rule("legacy/billing/foo").Enforce(arch.Types.MustHavePrefix("I")).Because("Reason.");
        }
    }

    private sealed class DanglingRuleSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("area/dangling");
        }
    }

    private sealed class MissingBecauseRuleSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("area/rule").Enforce(arch.Types.MustHavePrefix("I"));
        }
    }

    private sealed class MissingBecauseScopeSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Scope("legacy/billing").Freeze(arch.Namespace("MyApp.Legacy.Billing.*")).Dragons("Dragons.");
        }
    }

    private sealed class MissingDragonsSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Scope("legacy/billing").Freeze(arch.Namespace("MyApp.Legacy.Billing.*")).Because("Frozen.");
        }
    }

    private sealed class BlankDescriptionSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("area/rule").Enforce(arch.Types.Must(_ => true, "")).Because("Reason.");
        }
    }

    private sealed class MultiLineBecauseSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("area/rule").Enforce(arch.Types.MustHavePrefix("I")).Because("line one\nline two");
        }
    }

    private sealed class RepeatedBecauseSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("area/rule").Enforce(arch.Types.MustHavePrefix("I")).Because("First.").Because("Second.");
        }
    }

    private sealed class MalformedIdSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("Bad_Id").Enforce(arch.Types.MustHavePrefix("I")).Because("Reason.");
        }
    }

    private sealed class EmptyBoundarySpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Scope("legacy/billing").Freeze(arch.Namespace("MyApp.Legacy.Billing.*"))
                .BoundaryOnlyVia().Dragons("Dragons.").Because("Frozen.");
        }
    }

    private sealed class DuplicateLayerSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Layer("Dup", "MyApp.A.*");
            arch.Layer("Dup", "MyApp.B.*");
        }
    }

    private sealed class ForeignSelectionSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            var other = new Arch();
            Selection foreign = other.Types.OfKind(TypeKind.Interface);
            arch.Rule("area/rule").Enforce(foreign.MustHavePrefix("I")).Because("Reason.");
        }
    }

    private sealed class BlankMemberNameSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(arch.Member(typeof(DateTime), " "))).Because("Reason.");
        }
    }

    private sealed class TypoMemberSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(arch.Member(typeof(DateTime), "Nows"))).Because("Reason.");
        }
    }

    private sealed class BaseTypeMemberSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            // Wait lives on the non-generic base Task, not on Task<TResult> — the base-type guidance case.
            arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(arch.Member(typeof(Task<>), "Wait"))).Because("Reason.");
        }
    }

    private sealed class ForeignMemberSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            var other = new Arch();
            Member foreign = other.Member(typeof(DateTime), nameof(DateTime.Now));
            arch.Rule("area/rule").Enforce(arch.Types.MustNotUse(foreign)).Because("Reason.");
        }
    }

    private sealed class ValidMemberUseSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("time/inject-clock")
                .Migrate(
                    "Code reads the ambient clock directly.",
                    arch.Types.MustNotUse(
                        arch.Member(typeof(DateTime), nameof(DateTime.Now)),
                        arch.Member(typeof(DateTime), nameof(DateTime.UtcNow))))
                .Because("Wall-clock reads are untestable; inject IClock — ADR-nnn.")
                .Fix("Take IClock in the constructor; see OrderService for the pattern.");
        }
    }

    private sealed class ClosedGenericReturningSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            // typeof(Task<int>) is a closed construction — refused; ban the open definition instead.
            arch.Rule("area/rule")
                .Enforce(arch.Types.Methods.Returning(typeof(Task<int>)).MustHaveSuffix("Async"))
                .Because("Reason.");
        }
    }

    private sealed class BlankMemberWhereSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("area/rule")
                .Enforce(arch.Types.Methods.Where(_ => true, "").MustHaveSuffix("Async"))
                .Because("Reason.");
        }
    }

    private sealed class BlankMemberMustSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("area/rule").Enforce(arch.Types.Methods.Must(_ => true, "")).Because("Reason.");
        }
    }

    private sealed class ValidMemberSubjectSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            Selection web = arch.Namespace("MyApp.Web.*");
            arch.Rule("naming/async-suffix")
                .Enforce(web.Methods.Returning(typeof(Task), typeof(Task<>)).MustHaveSuffix("Async"))
                .Because("Async methods are discovered by suffix.");
        }
    }

    private sealed class MultipleProblemsSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            // Bad_Id → MalformedId; no .Because → MissingBecause; dangling scope → DanglingAnchor.
            arch.Rule("Bad_Id").Enforce(arch.Types.MustHavePrefix("I"));
            arch.Scope("other/scope");
        }
    }
}