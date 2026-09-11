using Shouldly;
using Xunit;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Hosting;
using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     Fail-closed dispatch: an unknown vocabulary member — a noun, a selection adjective, or a member
///     adjective the evaluator switches do not handle — must throw loudly naming the runtime type, and
///     <see cref="ArchChecker" /> must contain that throw as a per-rule <see cref="ViolationKind.RuleError" />
///     (the run continues, the rule reports Failed) rather than crash or read green. The
///     <c>private protected</c> <see cref="Constraint" />/<c>MemberConstraint</c> hierarchies cannot be
///     subclassed across the <c>InternalsVisibleTo</c> boundary, so the two constraint-level default arms
///     are reached through the selection they evaluate: a real verb over a fake-adjective subject routes the
///     throw through <c>ConstraintEvaluator.Evaluate</c> into <c>CheckRule</c>'s catch (the containment test).
/// </summary>
public sealed class EvaluatorFailClosedTests
{
    private static readonly CodebaseModel EmptyCodebase = new([], [], [], [], [], [], [], [], [], [], [], []);

    [Fact]
    public void ByNoun_UnknownNoun_ThrowsNamingTheType()
    {
        var selection = new RefinedSelection(new Arch(), new UnknownNoun(), Array.Empty<SelectionAdjective>());
        var evaluator = new SelectionEvaluator(EmptyCodebase);

        var ex = Should.Throw<InvalidOperationException>(() => evaluator.Evaluate(selection, SelectionPosition.Subject));
        ex.Message.ShouldBe("Unhandled selection noun 'UnknownNoun'.");
    }

    [Fact]
    public void ApplyAdjective_UnknownAdjective_ThrowsNamingTheType()
    {
        var selection = new RefinedSelection(
            new Arch(), TypesNoun.Instance, [new UnknownAdjective()]);
        var evaluator = new SelectionEvaluator(EmptyCodebase);

        var ex = Should.Throw<InvalidOperationException>(() => evaluator.Evaluate(selection, SelectionPosition.Subject));
        ex.Message.ShouldBe("Unhandled selection adjective 'UnknownAdjective'.");
    }

    [Fact]
    public void MemberApplyAdjective_UnknownMemberAdjective_ThrowsNamingTheType()
    {
        var arch = new Arch();
        var memberSelection = new KindMemberSelection(
            arch.Types, MemberKindFilter.Any, [new UnknownMemberAdjective()]);
        var selections = new SelectionEvaluator(EmptyCodebase);
        HashSet<TypeNode> sourceTypes = selections.Evaluate(memberSelection.Source, SelectionPosition.Subject);

        var ex = Should.Throw<InvalidOperationException>(() => MemberSelectionEvaluator.Resolve(memberSelection, sourceTypes));
        ex.Message.ShouldBe("Unhandled member adjective 'UnknownMemberAdjective'.");
    }

    [Fact]
    public void UnknownAdjectiveInRule_IsContainedAsRuleErrorNotCrash()
    {
        // A real verb over a fake-adjective subject: the throw propagates out of ConstraintEvaluator and is
        // contained by ArchChecker.CheckRule as a per-rule RuleError (the run continues; the rule is Failed).
        var arch = new Arch();
        var selection = new RefinedSelection(arch, TypesNoun.Instance, [new UnknownAdjective()]);
        Constraint constraint = selection.MustHavePrefix("I");
        ArchitectureModel model = Checker.HandBuilt("area/rule", constraint);

        RuleResult result = ArchChecker.Check(model, EmptyCodebase)
            .Results.Single();

        result.ShouldHaveFailedWithDetailContaining(ViolationKind.RuleError, "Unhandled selection adjective 'UnknownAdjective'.");
    }

    [Fact]
    public void UnknownMemberAdjectiveInRule_IsContainedAsRuleErrorNotCrash()
    {
        // The member-dispatch twin of UnknownAdjectiveInRule_IsContainedAsRuleErrorNotCrash: a member constraint
        // whose member subject carries a fake adjective routes the throw out of ConstraintEvaluator.Evaluate's
        // member branch (it dispatches MemberConstraints to EvaluateMember, which resolves the member subject) and
        // is contained by ArchChecker.CheckRule as a per-rule RuleError (the run continues; the rule is Failed).
        // The member-switch default arm itself is unreachable — MemberConstraint's private protected constructor
        // blocks a fake subclass across the InternalsVisibleTo boundary — so, exactly as on the type side, the
        // fail-closed containment guarantee is exercised through the selection the member verb evaluates.
        var arch = new Arch();
        var memberSelection = new KindMemberSelection(
            arch.Types, MemberKindFilter.Any, [new UnknownMemberAdjective()]);
        Constraint constraint = memberSelection.MustHaveSuffix("Async");
        ArchitectureModel model = Checker.HandBuilt("area/rule", constraint);

        RuleResult result = ArchChecker.Check(model, EmptyCodebase)
            .Results.Single();

        result.ShouldHaveFailedWithDetailContaining(ViolationKind.RuleError, "Unhandled member adjective 'UnknownMemberAdjective'.");
    }

    [Fact]
    public void ProjectApplyAdjective_UnknownProjectAdjective_ThrowsNamingTheType()
    {
        var selection = new RefinedProjectSelection(new Arch(), [new UnknownProjectAdjective()]);

        var ex = Should.Throw<InvalidOperationException>(() => ProjectSelectionEvaluator.Resolve(selection, EmptyCodebase.Projects));
        ex.Message.ShouldBe("Unhandled project adjective 'UnknownProjectAdjective'.");
    }

    [Fact]
    public void UnknownProjectAdjectiveInRule_IsContainedAsRuleErrorNotCrash()
    {
        // The project-dispatch twin of the two rows above: a project verb over a fake-adjective subject
        // routes the throw out of ConstraintEvaluator.Evaluate's project branch and is contained by
        // ArchChecker.CheckRule as a per-rule RuleError. The project-switch default arm itself is
        // unreachable for the same reason the member one is — ProjectConstraint's private protected
        // constructor blocks a fake subclass across the InternalsVisibleTo boundary — so the containment
        // guarantee is exercised through the selection the project verb evaluates.
        var arch = new Arch();
        var selection = new RefinedProjectSelection(arch, [new UnknownProjectAdjective()]);
        Constraint constraint = selection.MustNotBePackable();
        ArchitectureModel model = Checker.HandBuilt("area/rule", constraint);

        RuleResult result = ArchChecker.Check(model, EmptyCodebase)
            .Results.Single();

        result.ShouldHaveFailedWithDetailContaining(ViolationKind.RuleError, "Unhandled project adjective 'UnknownProjectAdjective'.");
    }

    [Fact]
    public void UnionSelection_Noun_ThrowsBecauseUnionHasNoSingleNoun()
    {
        // A UnionSelection is a union of selections (GRAMMAR §5.1), so it has no single noun: it renders
        // through its own §6 arm, collapsed or or-joined. Reading .Noun still throws rather than inventing
        // one — a subject union is admitted by the renderer's union arm, not by minting a fake head.
        var arch = new Arch();
        var union = new UnionSelection(arch, [arch.Types]);

        var ex = Should.Throw<InvalidOperationException>(() => _ = union.Noun);
        ex.Message.ShouldBe("A union selection has no single noun; render it in reference position.");
    }

    private sealed class UnknownNoun : SelectionNoun
    {
        internal override string Locative => string.Empty;
    }

    private sealed class UnknownAdjective : SelectionAdjective
    {
        internal override AdjectivePlacement Placement => AdjectivePlacement.Inline;

        internal override string Fragment => string.Empty;
    }

    private sealed class UnknownMemberAdjective : MemberAdjective
    {
        internal override AdjectivePlacement Placement => AdjectivePlacement.Inline;

        internal override string Fragment => string.Empty;
    }

    private sealed class UnknownProjectAdjective : ProjectAdjective
    {
        internal override AdjectivePlacement Placement => AdjectivePlacement.Inline;

        internal override string Fragment => string.Empty;
    }
}
