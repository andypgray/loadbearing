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
            arch.Rule("area/rule").Enforce(arch.Types.Must(t => true, "")).Because("Reason.");
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