using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing.Checking;

/// <summary>
///     Evaluates one <see cref="Constraint" /> against the codebase, per-verb (GRAMMAR §4.1, §4.3,
///     §4.5, §5.3). Dependency verbs walk <see cref="CodebaseModel.Edges" />; the member verb
///     (<c>MustNotUse</c>) walks <see cref="CodebaseModel.MemberEdges" />; shape verbs test each
///     subject. Every verb first requires a non-empty subject set — an empty subject fails the rule by
///     default (GRAMMAR §4.1). Returns violations (unordered; the caller sorts) and any inert-target warnings.
/// </summary>
internal sealed class ConstraintEvaluator
{
    /// <summary>The pinned message on an empty-subject failure (ArchUnit precedent, GRAMMAR §4.1).</summary>
    internal const string EmptySubjectMessage = "The subject selection matched no solution-declared types.";

    /// <summary>The pinned message on an empty <em>member</em> subject failure (the member analog, GRAMMAR §4.6).</summary>
    internal const string EmptyMemberSubjectMessage = "The subject selection matched no solution-declared members.";

    private static readonly IReadOnlyList<CheckWarning> NoWarnings = Array.Empty<CheckWarning>();

    private readonly IReadOnlyList<ReferenceEdge> _edges;
    private readonly IReadOnlyList<MemberEdge> _memberEdges;
    private readonly MemberSelectionEvaluator _memberSelections;
    private readonly SelectionEvaluator _selections;

    internal ConstraintEvaluator(CodebaseModel model)
    {
        _edges = model.Edges;
        _memberEdges = model.MemberEdges;
        _selections = new SelectionEvaluator(model);
        _memberSelections = new MemberSelectionEvaluator(_selections);
    }

    internal (IReadOnlyList<Violation> Violations, IReadOnlyList<CheckWarning> Warnings) Evaluate(Constraint constraint)
    {
        // A member-subject constraint (GRAMMAR §4.6) ranges over declared members, so it dispatches before
        // the type-subject gate: its own empty check speaks in member terms (a type subject that matches
        // types none of whose members survive the kind filter is the ordinary way to fail empty).
        if (constraint is MemberConstraint memberConstraint) return EvaluateMember(memberConstraint);

        var subjects = _selections.Evaluate(constraint.Subject, SelectionPosition.Subject);
        if (subjects.Count == 0) return (new[] { Violation.EmptySubject(EmptySubjectMessage) }, NoWarnings);

        switch (constraint)
        {
            case MustNotReferenceConstraint c:
                return ForbiddenReference(subjects, c.Targets, false);
            case MustNotBeReferencedByConstraint c:
                return ForbiddenReference(subjects, c.Sources, true);
            case MustOnlyReferenceConstraint c:
                return OnlyReference(subjects, c.Targets);
            case MustOnlyBeReferencedByConstraint c:
                return OnlyBeReferencedBy(subjects, c.Sources);
            case MustNotUseConstraint c:
                return ForbiddenMemberUse(subjects, c.Members);
            case MustResideInNamespaceConstraint c:
                var namespacePattern = new NamespacePattern(c.Glob);
                return Shape(subjects, t => namespacePattern.Matches(t.Namespace));
            case MustHaveSuffixConstraint c:
                return Shape(subjects, t => t.Name.EndsWith(c.Suffix, StringComparison.Ordinal));
            case MustHavePrefixConstraint c:
                return Shape(subjects, t => t.Name.StartsWith(c.Prefix, StringComparison.Ordinal));
            case MustHaveNameMatchingConstraint c:
                var namePattern = new TypeNamePattern(c.Glob);
                return Shape(subjects, t => namePattern.Matches(t.Name));
            case MustImplementConstraint c:
                return Shape(subjects, SelectionEvaluator.InterfaceMatcher(c.Type));
            case MustDeriveFromConstraint c:
                return Shape(subjects, SelectionEvaluator.BaseTypeMatcher(c.Type));
            case MustBeAttributedWithConstraint c:
                return Shape(subjects, SelectionEvaluator.AttributeMatcher(c.Type));
            case MustBeSealedConstraint:
                return Shape(subjects, t => t.IsSealed);
            case MustBeStaticConstraint:
                return Shape(subjects, t => t.IsStatic);
            case MustBeAbstractConstraint:
                return Shape(subjects, t => t.IsAbstract);
            case MustBePublicConstraint:
                return Shape(subjects, t => t.Accessibility == Accessibility.Public);
            case MustBeInternalConstraint:
                return Shape(subjects, t => t.Accessibility == Accessibility.Internal);
            case MustConstraint c:
                return Shape(subjects, t => SelectionEvaluator.InvokePredicate(c.Predicate, t, "Must"));
            default:
                return (Array.Empty<Violation>(), NoWarnings);
        }
    }

    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) ForbiddenReference(
        HashSet<TypeNode> subjects, IReadOnlyList<Selection> operands, bool inbound)
    {
        var operandSet = ResolveOperands(operands);
        var violations = new List<Violation>();

        // Outbound (MustNotReference): edge subject→operand. Inbound (MustNotBeReferencedBy):
        // edge operand→subject, Source = the referencing type where the edit happens (GRAMMAR §4.3).
        foreach (ReferenceEdge edge in _edges)
        {
            bool hit = inbound
                ? subjects.Contains(edge.Target) && operandSet.Contains(edge.Source)
                : subjects.Contains(edge.Source) && operandSet.Contains(edge.Target);
            if (hit) violations.Add(Violation.Reference(edge.Source, edge.Target, edge.Sites));
        }

        // Inert only when the forbidden operand set is empty AND at least one operand is a pattern
        // selection; a bare typeof target absent from the codebase is the win condition (decision 3).
        var warnings = violations.Count == 0 && operandSet.Count == 0 && operands.Any(SelectionEvaluator.IsPatternSelection)
            ? new[] { new CheckWarning(CheckWarningKind.InertTarget, "This rule is inert: its target selection matched no types.") }
            : NoWarnings;

        return (violations, warnings);
    }

    // The member-access verb (GRAMMAR §4.5): a member edge is a hit when its source is a subject AND
    // its used member matches a banned (declaring type, name) pair — ordinal, one ban covering every
    // overload. Per-overload edges yield per-overload MemberUse violations (the §4.3 identity substrate).
    // The banned set is resolved eagerly so a closed-generic member anchor is refused (RuleError) before
    // any edge is tested — mirroring the type-noun refusal (decision 2).
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) ForbiddenMemberUse(
        HashSet<TypeNode> subjects, IReadOnlyList<Member> members)
    {
        var banned = new HashSet<(string DeclaringType, string Name)>();
        foreach (Member member in members)
        {
            string declaringType = SelectionEvaluator.DefinitionFullName(
                member.DeclaringType,
                "member-use edges are definition-level. Anchor the member on the open definition instead.");
            banned.Add((declaringType, member.Name));
        }

        var violations = new List<Violation>();
        foreach (MemberEdge edge in _memberEdges)
            if (subjects.Contains(edge.Source) && banned.Contains((edge.Member.ContainingType.FullName, edge.Member.Name)))
                violations.Add(Violation.MemberUse(edge.Source, edge.Member, edge.Sites));

        // MustNotUse never warns: member targets are typeof-anchored (no pattern form), so a banned member
        // absent from the codebase is the win condition, exactly like a bare typeof target (GRAMMAR §4.5).
        return (violations, NoWarnings);
    }

    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) OnlyReference(
        HashSet<TypeNode> subjects, IReadOnlyList<Selection> allowedTargets)
    {
        var allowed = ResolveOperands(allowedTargets);
        var violations = new List<Violation>();

        // Strict, no implicit self-allowance; external targets are exempt (the complement universe is
        // solution-declared, GRAMMAR §4.1). MustOnly* never warns — an empty allow-set is loud by itself.
        foreach (ReferenceEdge edge in _edges)
            if (subjects.Contains(edge.Source) && !edge.Target.IsExternal && !allowed.Contains(edge.Target))
                violations.Add(Violation.Reference(edge.Source, edge.Target, edge.Sites));

        return (violations, NoWarnings);
    }

    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) OnlyBeReferencedBy(
        HashSet<TypeNode> subjects, IReadOnlyList<Selection> allowedSources)
    {
        var allowed = ResolveOperands(allowedSources);
        var violations = new List<Violation>();

        // Any inbound reference from outside the allow-set is a violation (the containment verb, §7).
        // Edge sources are always solution-declared, so no external caveat is needed.
        foreach (ReferenceEdge edge in _edges)
            if (subjects.Contains(edge.Target) && !allowed.Contains(edge.Source))
                violations.Add(Violation.Reference(edge.Source, edge.Target, edge.Sites));

        return (violations, NoWarnings);
    }

    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) Shape(HashSet<TypeNode> subjects, Func<TypeNode, bool> holds)
    {
        var violations = new List<Violation>();
        foreach (TypeNode subject in subjects)
            if (!holds(subject))
                violations.Add(Violation.Shape(subject, subject.DeclarationSites));

        return (violations, NoWarnings);
    }

    // The member modal verbs (GRAMMAR §5.7): resolve the member subject, then test each surviving member
    // against the verb's shape/naming/accessibility/flag predicate — a failing member is a MemberShape
    // violation at its own declaration sites, identity keyed on its DocId (§4.6). An empty member subject
    // fails with the member-flavored message (the analog of the empty type subject). Resolution can throw
    // RuleEvaluationException (a closed-generic .Returning anchor); ArchChecker turns that into a RuleError.
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) EvaluateMember(MemberConstraint constraint)
    {
        var members = _memberSelections.Resolve(constraint.MemberSubject);
        if (members.Count == 0) return (new[] { Violation.EmptySubject(EmptyMemberSubjectMessage) }, NoWarnings);

        switch (constraint)
        {
            case MemberMustHaveSuffixConstraint c:
                return MemberShape(members, m => m.Name.EndsWith(c.Suffix, StringComparison.Ordinal));
            case MemberMustHavePrefixConstraint c:
                return MemberShape(members, m => m.Name.StartsWith(c.Prefix, StringComparison.Ordinal));
            case MemberMustHaveNameMatchingConstraint c:
                var namePattern = new TypeNamePattern(c.Glob);
                return MemberShape(members, m => namePattern.Matches(m.Name));
            case MemberMustBePublicConstraint:
                return MemberShape(members, m => m.Accessibility == Accessibility.Public);
            case MemberMustBeInternalConstraint:
                return MemberShape(members, m => m.Accessibility == Accessibility.Internal);
            case MemberMustBePrivateConstraint:
                return MemberShape(members, m => m.Accessibility == Accessibility.Private);
            case MemberMustBeStaticConstraint:
                return MemberShape(members, m => m.IsStatic);
            case MemberMustBeAbstractConstraint:
                return MemberShape(members, m => m.IsAbstract);
            case MemberMustBeVirtualConstraint:
                return MemberShape(members, m => m.IsVirtual);
            case MemberMustConstraint c:
                return MemberShape(members, m => SelectionEvaluator.InvokePredicate(c.Predicate, m, "Must"));
            default:
                return (Array.Empty<Violation>(), NoWarnings);
        }
    }

    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) MemberShape(
        IReadOnlyList<MemberNode> members, Func<IMemberInfo, bool> holds)
    {
        var violations = new List<Violation>();
        foreach (MemberNode member in members)
            if (!holds(member))
                violations.Add(Violation.MemberShape(member, member.DeclarationSites));

        return (violations, NoWarnings);
    }

    private HashSet<TypeNode> ResolveOperands(IReadOnlyList<Selection> operands)
    {
        var set = new HashSet<TypeNode>();
        foreach (Selection operand in operands) set.UnionWith(_selections.Evaluate(operand, SelectionPosition.Target));

        return set;
    }
}