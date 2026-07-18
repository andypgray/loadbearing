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
    public void RepeatedPosture_PostureVerbTwiceViaStoredRuleBuilder_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new DoublePostureRuleSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.RepeatedPosture && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.RepeatedPosture).Message
            .ShouldBe("Rule 'area/rule' has more than one posture; call .Enforce(...) or .Migrate(...) exactly once.");
    }

    [Fact]
    public void RepeatedPosture_FreezeTwiceViaStoredScopeBuilder_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new DoubleFreezeScopeSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.RepeatedPosture && e.RuleId == "legacy/billing");
        ex.Errors.First(e => e.Code == Code.RepeatedPosture).Message
            .ShouldBe("Scope 'legacy/billing' has more than one posture; call .Freeze(...) exactly once.");
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

    [Fact]
    public void BlankPattern_BlankNamespaceGlob_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new BlankNamespaceGlobSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.BlankPattern && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.BlankPattern).Message.ShouldBe("Blank namespace pattern on 'area/rule'.");
    }

    [Fact]
    public void BlankPattern_BlankSuffix_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new BlankSuffixSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.BlankPattern && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.BlankPattern).Message.ShouldBe("Blank suffix on 'area/rule'.");
    }

    [Fact]
    public void BlankPattern_BlankMemberSubjectAffix_IsReported()
    {
        // The member-subject adjective walk (parallel to the member prose/Returning walks) reaches a
        // blank member .WithSuffix (GRAMMAR §8 item 15, §4.6).
        SpecValidationException ex = BuildExpectingFailure(new BlankMemberSuffixSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.BlankPattern && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.BlankPattern).Message.ShouldBe("Blank member suffix on 'area/rule'.");
    }

    [Fact]
    public void UnanchoredSubtreePattern_WildcardInSubtreePrefix_IsReported()
    {
        SpecValidationException ex = BuildExpectingFailure(new DeadSubtreeGlobSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.UnanchoredSubtreePattern && e.RuleId == "area/rule");
        ex.Errors.First(e => e.Code == Code.UnanchoredSubtreePattern).Message
            .ShouldBe("The namespace pattern 'MyApp.*.Controllers.*' on 'area/rule' has a trailing `.*` subtree " +
                      "operator but its literal prefix contains a `*`, which never matches; anchor the subtree on a literal prefix.");
    }

    [Fact]
    public void UnanchoredSubtreePattern_OnLayerGlob_IsReportedSpecWide()
    {
        SpecValidationException ex = BuildExpectingFailure(new DeadSubtreeLayerSpec());

        ex.Errors.ShouldContain(e => e.Code == Code.UnanchoredSubtreePattern && e.RuleId == null);
        ex.Errors.First(e => e.Code == Code.UnanchoredSubtreePattern).Message
            .ShouldBe("The namespace pattern 'MyApp.*.Svc.*' on layer 'Bad' has a trailing `.*` subtree " +
                      "operator but its literal prefix contains a `*`, which never matches; anchor the subtree on a literal prefix.");
    }

    [Fact]
    public void NamespacePattern_InteriorWildcardWithoutSubtree_BuildsWithoutError()
    {
        // MyApp.*.Orders is legitimate single-segment matching (GRAMMAR §4.2), not a dead subtree pattern.
        Should.NotThrow(() => ArchModelBuilder.Build(new InteriorWildcardSpec()));
    }

    [Fact]
    public void AllErrors_ThreeBadPatterns_AreReportedInOnePass()
    {
        SpecValidationException ex = BuildExpectingFailure(new ThreeBadPatternsSpec());

        // Two dead subtree globs (the subject noun and the verb) plus one blank affix, reported together.
        ex.Errors.Count(e => e.Code == Code.UnanchoredSubtreePattern).ShouldBe(2);
        ex.Errors.Count(e => e.Code == Code.BlankPattern).ShouldBe(1);
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

    private sealed class DoublePostureRuleSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            // The stage machine forbids the fluent double-call, but a stored IRuleBuilder is mutable, so a
            // second posture verb silently overwrites the first (§8 item 17). Only one .Because so the
            // repeated posture is the sole error.
            IRuleBuilder rule = arch.Rule("area/rule");
            rule.Enforce(arch.Types.MustHavePrefix("I"));
            rule.Migrate("Controllers open SqlConnection directly.", arch.Types.MustHaveSuffix("Handler"))
                .Because("Reason.");
        }
    }

    private sealed class DoubleFreezeScopeSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            // A stored IScopeBuilder re-called with .Freeze silently overwrites the frozen selection (§8 item 17).
            IScopeBuilder scope = arch.Scope("legacy/billing");
            scope.Freeze(arch.Namespace("MyApp.Legacy.Billing.*"));
            scope.Freeze(arch.Namespace("MyApp.Legacy.Other.*")).Dragons("Dragons.").Because("Frozen.");
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

    private sealed class BlankNamespaceGlobSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("area/rule").Enforce(arch.Namespace(" ").MustHavePrefix("I")).Because("Reason.");
        }
    }

    private sealed class BlankSuffixSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("area/rule").Enforce(arch.Types.MustHaveSuffix(" ")).Because("Reason.");
        }
    }

    private sealed class BlankMemberSuffixSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("area/rule").Enforce(arch.Types.Methods.WithSuffix(" ").MustHaveSuffix("Async")).Because("Reason.");
        }
    }

    private sealed class DeadSubtreeGlobSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("area/rule").Enforce(arch.Namespace("MyApp.*.Controllers.*").MustHavePrefix("I")).Because("Reason.");
        }
    }

    private sealed class DeadSubtreeLayerSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Layer("Bad", "MyApp.*.Svc.*");
        }
    }

    private sealed class InteriorWildcardSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("area/rule").Enforce(arch.Namespace("MyApp.*.Orders").MustHavePrefix("I")).Because("Reason.");
        }
    }

    private sealed class ThreeBadPatternsSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("area/rule")
                .Enforce(arch.Namespace("MyApp.*.A.*").WithSuffix(" ").MustResideInNamespace("Bad.*.X.*"))
                .Because("Reason.");
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