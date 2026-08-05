using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Model;
using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Checking;

/// <summary>
///     Evaluates one <see cref="Constraint" /> against the codebase, per-verb (GRAMMAR §4.1, §4.3,
///     §4.5, §4.7, §4.8, §4.9, §5.3). Dependency verbs walk <see cref="CodebaseModel.Edges" />; the member verb
///     (<c>MustNotUse</c>) walks <see cref="CodebaseModel.MemberEdges" />; the construction verb
///     (<c>MustNotConstruct</c>) walks <see cref="CodebaseModel.ConstructorEdges" />; the injection verb
///     (<c>MustNotInject</c>) walks <see cref="CodebaseModel.InjectionEdges" />; the catch verbs
///     (<c>MustNotCatch</c>, <c>MustNotCatchUnfiltered</c>, <c>MustNotSwallow</c>) walk
///     <see cref="CodebaseModel.CatchEdges" />; the
///     throw verbs (<c>MustOnlyThrow</c>, <c>MustNotThrow</c>) walk <see cref="CodebaseModel.ThrowEdges" />;
///     the exposure verb (<c>MustNotExpose</c>) walks <see cref="CodebaseModel.ExposureEdges" />; shape verbs
///     test each subject. Every verb first requires a non-empty subject set — an empty subject fails the rule
///     by default (GRAMMAR §4.1). Returns violations (unordered; the caller sorts) and any inert-target warnings.
/// </summary>
internal sealed class ConstraintEvaluator
{
    /// <summary>The pinned message on an empty-subject failure (ArchUnit precedent, GRAMMAR §4.1).</summary>
    internal const string EmptySubjectMessage = "The subject selection matched no solution-declared types.";

    /// <summary>The pinned message on an empty <em>member</em> subject failure (the member analog, GRAMMAR §4.6).</summary>
    internal const string EmptyMemberSubjectMessage = "The subject selection matched no solution-declared members.";

    private static readonly IReadOnlyList<CheckWarning> NoWarnings = Array.Empty<CheckWarning>();

    private readonly IReadOnlyList<CatchEdge> _catchEdges;
    private readonly IReadOnlyList<ConstructorEdge> _constructorEdges;
    private readonly IReadOnlyList<ReferenceEdge> _edges;
    private readonly IReadOnlyList<ExposureEdge> _exposureEdges;
    private readonly IReadOnlyList<InjectionEdge> _injectionEdges;
    private readonly IReadOnlyList<MemberEdge> _memberEdges;
    private readonly MemberSelectionEvaluator _memberSelections;
    private readonly SelectionEvaluator _selections;
    private readonly IReadOnlyList<ThrowEdge> _throwEdges;

    internal ConstraintEvaluator(CodebaseModel model)
    {
        _edges = model.Edges;
        _memberEdges = model.MemberEdges;
        _constructorEdges = model.ConstructorEdges;
        _injectionEdges = model.InjectionEdges;
        _catchEdges = model.CatchEdges;
        _throwEdges = model.ThrowEdges;
        _exposureEdges = model.ExposureEdges;
        _selections = new SelectionEvaluator(model);
        _memberSelections = new MemberSelectionEvaluator(_selections);
    }

    /// <summary>
    ///     The pinned message on an empty union <em>operand</em> in subject position (GRAMMAR §9): a typo'd
    ///     project name inside a four-way union must not be masked by its siblings, so the operand that
    ///     matched nothing is named in its own right.
    /// </summary>
    internal static string EmptyOperandMessage(string operandReference)
    {
        return $"The subject selection operand \"{operandReference}\" matched no solution-declared types.";
    }

    internal (IReadOnlyList<Violation> Violations, IReadOnlyList<CheckWarning> Warnings) Evaluate(Constraint constraint)
    {
        // Loud per-operand emptiness for a union subject (GRAMMAR §9), ahead of the member dispatch because
        // MemberConstraint.Subject IS the underlying type selection — so one gate covers the type- and
        // member-subject paths alike.
        var emptyOperands = EmptySubjectOperands(constraint.Subject);
        if (emptyOperands.Count > 0) return (emptyOperands, NoWarnings);

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
            case MustNotConstructConstraint c:
                return ForbiddenConstruction(subjects, c.Targets);
            case MustNotInjectConstraint c:
                return ForbiddenInjection(subjects, c.Targets);
            case MustNotCatchConstraint c:
                return ForbiddenCatch(subjects, c.Targets);
            case MustNotCatchUnfilteredConstraint c:
                return ForbiddenUnfilteredCatch(subjects, c.Targets);
            case MustNotSwallowConstraint c:
                return ForbiddenSwallow(subjects, c.Targets);
            case MustNotExposeConstraint c:
                return ForbiddenExposure(subjects, c.Targets);
            case MustOnlyThrowConstraint c:
                return OnlyThrow(subjects, c.Targets);
            case MustNotThrowConstraint c:
                return ForbiddenThrow(subjects, c.Targets);
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
                return Shape(subjects, SelectionEvaluator.InterfaceMatcher(c.Anchor));
            case MustDeriveFromConstraint c:
                return Shape(subjects, SelectionEvaluator.BaseTypeMatcher(c.Anchor));
            case MustBeAttributedWithConstraint c:
                return Shape(subjects, SelectionEvaluator.AttributeMatcher(c.Anchor));
            case MustNotImplementConstraint c:
                return Shape(subjects, NoneOf(c.Anchors, SelectionEvaluator.InterfaceMatcher));
            case MustNotDeriveFromConstraint c:
                return Shape(subjects, NoneOf(c.Anchors, SelectionEvaluator.BaseTypeMatcher));
            case MustNotBeAttributedWithConstraint c:
                return Shape(subjects, NoneOf(c.Anchors, SelectionEvaluator.AttributeMatcher));
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
                // Fail closed: the closed Constraint hierarchy makes this arm unreachable for any v1
                // verb, so an unknown subclass means a new verb shipped without a switch arm. Surface it
                // loudly — ArchChecker.CheckRule contains the throw as a per-rule RuleError — rather than
                // passing the rule silently (a missing arm must never read green).
                throw new InvalidOperationException($"Unhandled constraint '{constraint.GetType().Name}'.");
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
        // selection; a bare typeof target absent from the codebase is the win condition, not a warning.
        var warnings = violations.Count == 0 && operandSet.Count == 0 && operands.Any(SelectionEvaluator.IsPatternSelection)
            ? new[] { new CheckWarning(CheckWarningKind.InertTarget, "This rule is inert: its target selection matched no types.") }
            : NoWarnings;

        return (violations, warnings);
    }

    // The member-access verb (GRAMMAR §4.5): a member edge is a hit when its source is a subject AND
    // its used member matches a banned (declaring type, name) pair — ordinal, one ban covering every
    // overload. Per-overload edges yield per-overload MemberUse violations (the §4.3 identity substrate).
    // The banned set is resolved eagerly so a closed-generic member anchor is refused (RuleError) before
    // any edge is tested — mirroring the type-noun refusal in SelectionEvaluator.DefinitionFullName.
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

        // MustNotUse never warns: member targets are concrete (type, name) anchors (no pattern form), so a banned member
        // absent from the codebase is the win condition, exactly like a bare typeof target (GRAMMAR §4.5).
        return (violations, NoWarnings);
    }

    // The construction verb (GRAMMAR §4.5, §5.3): a construction edge is a hit when its source is a subject
    // AND the constructed type is a forbidden operand — the "you may use it; you may not create it" ban.
    // Extraction already collapses every `new` of one type into one (source, constructed) edge with its sites
    // aggregated, so one edge yields one Construction violation keyed on the type pair (overload-indifferent,
    // §4.3). Inert-target warning semantics mirror ForbiddenReference exactly: a forbidden set that resolves
    // empty from a pattern operand can never fire, so it is loudly flagged inert (a bare typeof absent from
    // the codebase is the win condition, not a warning).
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) ForbiddenConstruction(
        HashSet<TypeNode> subjects, IReadOnlyList<Selection> operands)
    {
        var operandSet = ResolveOperands(operands);
        var violations = new List<Violation>();

        foreach (ConstructorEdge edge in _constructorEdges)
            if (subjects.Contains(edge.Source) && operandSet.Contains(edge.Constructed))
                violations.Add(Violation.Construction(edge.Source, edge.Constructed, edge.Sites));

        var warnings = violations.Count == 0 && operandSet.Count == 0 && operands.Any(SelectionEvaluator.IsPatternSelection)
            ? new[] { new CheckWarning(CheckWarningKind.InertTarget, "This rule is inert: its target selection matched no types.") }
            : NoWarnings;

        return (violations, warnings);
    }

    // The injection verb (GRAMMAR §4.7, §5.3): an injection edge is a hit when its source is a subject AND the
    // injected parameter type is a forbidden operand — the captive-dependency ban. Extraction already collapses
    // every constructor parameter typed on one injected type into one (source, injected) edge with its sites
    // aggregated, so one edge yields one Injection violation keyed on the type pair (constructor-overload- and
    // parameter-name-indifferent, §4.3). Unlike ForbiddenConstruction, MustNotInject NEVER warns: its natural
    // operand is a Registered selection (§4.7), and an empty Registered operand means no such registrations
    // exist — the win condition, exactly like a bare typeof target (GRAMMAR §4.1). So there is no inert-target
    // arm here; an empty operand set is silence, not a warning.
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) ForbiddenInjection(
        HashSet<TypeNode> subjects, IReadOnlyList<Selection> operands)
    {
        var operandSet = ResolveOperands(operands);
        var violations = new List<Violation>();

        foreach (InjectionEdge edge in _injectionEdges)
            if (subjects.Contains(edge.Source) && operandSet.Contains(edge.Injected))
                violations.Add(Violation.Injection(edge.Source, edge.Injected, edge.Sites));

        return (violations, NoWarnings);
    }

    // The catch verb (GRAMMAR §4.8, §5.3): a catch edge is a hit when its source is a subject AND the caught
    // type is a forbidden operand — the "you may throw it; you may not swallow it" ban. Matching is exact
    // definition-level FQN on the operand set (the shared HashSet<TypeNode> membership): MustNotCatch(
    // typeof(Exception)) flags only `catch (System.Exception)` and bare-catch edges (a bare catch already
    // synthesized System.Exception at extraction), never a narrower `catch (IOException)`. Inert-target warning
    // semantics mirror ForbiddenConstruction exactly: a forbidden set that resolves empty from a pattern operand
    // can never fire, so it is loudly flagged inert (a bare typeof absent from the codebase is the win condition,
    // not a warning).
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) ForbiddenCatch(
        HashSet<TypeNode> subjects, IReadOnlyList<Selection> operands)
    {
        var operandSet = ResolveOperands(operands);
        var violations = new List<Violation>();

        foreach (CatchEdge edge in _catchEdges)
            if (subjects.Contains(edge.Source) && operandSet.Contains(edge.Caught))
                violations.Add(Violation.Catch(edge.Source, edge.Caught, edge.Sites));

        var warnings = violations.Count == 0 && operandSet.Count == 0 && operands.Any(SelectionEvaluator.IsPatternSelection)
            ? new[] { new CheckWarning(CheckWarningKind.InertTarget, "This rule is inert: its target selection matched no types.") }
            : NoWarnings;

        return (violations, warnings);
    }

    // The filter-aware catch verb (GRAMMAR §4.8, §5.3): the same walk as ForbiddenCatch over the same edges,
    // decided on the edge's recorded UNFILTERED sites instead of its sites. A matching edge violates iff at
    // least one of its `catch` clauses spells no `when` filter, and the evidence is exactly those sites — so an
    // edge every one of whose sites is filtered is GREEN, and no printed file:line is ever a filtered site.
    // Extraction records the unfiltered subset as its own fact (§4.8), so this arm reads it directly as evidence
    // rather than subtracting one set from another (the polarity that keeps a same-line collision red-biased).
    // Identity is untouched — the (source, caught) type pair, the very key ForbiddenCatch's violations carry, so
    // the unfiltered sites are evidence and never identity (§4.3) and a baseline entry means the same thing under
    // either catch verb. Matching stays exact definition-level FQN and the inert-target warning is the §4.1
    // forbidden-set family's, both exactly as ForbiddenCatch.
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) ForbiddenUnfilteredCatch(
        HashSet<TypeNode> subjects, IReadOnlyList<Selection> operands)
    {
        var operandSet = ResolveOperands(operands);
        var violations = new List<Violation>();

        foreach (CatchEdge edge in _catchEdges)
            if (subjects.Contains(edge.Source) && operandSet.Contains(edge.Caught) && edge.UnfilteredSites.Count > 0)
                violations.Add(Violation.Catch(edge.Source, edge.Caught, edge.UnfilteredSites));

        var warnings = violations.Count == 0 && operandSet.Count == 0 && operands.Any(SelectionEvaluator.IsPatternSelection)
            ? new[] { new CheckWarning(CheckWarningKind.InertTarget, "This rule is inert: its target selection matched no types.") }
            : NoWarnings;

        return (violations, warnings);
    }

    // The rethrow-aware catch verb (GRAMMAR §4.8, §5.3): the same walk again over the same edges, decided on the
    // edge's recorded SWALLOWING sites — the ones that are unfiltered AND do not end in a throw. A matching edge
    // violates iff at least one of its clauses holds the failure and continues, and the evidence is exactly those
    // sites, so an edge whose every unfiltered clause rethrows or translates is GREEN and no printed file:line is
    // ever a rethrowing site. Extraction records this subset as its own fact for the same polarity reason as the
    // unfiltered subset (§4.8) — read the sites the ban forbids, never a complement. Identity is untouched: the
    // (source, caught) type pair, so a baseline entry means the same thing under all three catch verbs, the sites
    // stay evidence (§4.3), and narrowing which sites a violation prints never narrows what it keys. Matching
    // stays exact definition-level FQN and the inert-target warning is the §4.1 forbidden-set family's.
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) ForbiddenSwallow(
        HashSet<TypeNode> subjects, IReadOnlyList<Selection> operands)
    {
        var operandSet = ResolveOperands(operands);
        var violations = new List<Violation>();

        foreach (CatchEdge edge in _catchEdges)
            if (subjects.Contains(edge.Source) && operandSet.Contains(edge.Caught) && edge.SwallowingSites.Count > 0)
                violations.Add(Violation.Catch(edge.Source, edge.Caught, edge.SwallowingSites));

        var warnings = violations.Count == 0 && operandSet.Count == 0 && operands.Any(SelectionEvaluator.IsPatternSelection)
            ? new[] { new CheckWarning(CheckWarningKind.InertTarget, "This rule is inert: its target selection matched no types.") }
            : NoWarnings;

        return (violations, warnings);
    }

    // The throw-ban verb (GRAMMAR §4.8, §5.3): the ban polarity beside MustOnlyThrow's strict allow-list — a
    // throw edge is a hit when its source is a subject AND the thrown type is a forbidden operand, for the case
    // where the forbidden thrown types are enumerable and the permitted ones are not. Matching is exact
    // definition-level FQN on the operand set, so a ban on Exception never reaches a derived throw (the narrow
    // throw is the good state, mirroring the catch verbs). Inert-target warning semantics are the §4.1
    // forbidden-set family's, exactly as ForbiddenCatch — the point of departure from OnlyThrow, which never
    // warns because an empty allow-set is loud on its own.
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) ForbiddenThrow(
        HashSet<TypeNode> subjects, IReadOnlyList<Selection> operands)
    {
        var operandSet = ResolveOperands(operands);
        var violations = new List<Violation>();

        foreach (ThrowEdge edge in _throwEdges)
            if (subjects.Contains(edge.Source) && operandSet.Contains(edge.Thrown))
                violations.Add(Violation.Throw(edge.Source, edge.Thrown, edge.Sites));

        var warnings = violations.Count == 0 && operandSet.Count == 0 && operands.Any(SelectionEvaluator.IsPatternSelection)
            ? new[] { new CheckWarning(CheckWarningKind.InertTarget, "This rule is inert: its target selection matched no types.") }
            : NoWarnings;

        return (violations, warnings);
    }

    // The exposure verb (GRAMMAR §4.9, §5.3): an exposure edge is a hit when its source is a subject AND the
    // exposed type is a forbidden operand — the "you may use it; you may not surface it on your public API" ban.
    // Matching is exact definition-level FQN on the operand set (the shared HashSet<TypeNode> membership):
    // MustNotExpose(typeof(DataTable)) flags only a `DataTable` signature position, never a narrower `DataView`
    // one (no hierarchy-aware matching). Inert-target warning semantics mirror ForbiddenCatch exactly: a forbidden
    // set that resolves empty from a pattern operand can never fire, so it is loudly flagged inert (a bare typeof
    // absent from the codebase is the win condition, not a warning).
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) ForbiddenExposure(
        HashSet<TypeNode> subjects, IReadOnlyList<Selection> operands)
    {
        var operandSet = ResolveOperands(operands);
        var violations = new List<Violation>();

        foreach (ExposureEdge edge in _exposureEdges)
            if (subjects.Contains(edge.Source) && operandSet.Contains(edge.Exposed))
                violations.Add(Violation.Expose(edge.Source, edge.Exposed, edge.Sites));

        var warnings = violations.Count == 0 && operandSet.Count == 0 && operands.Any(SelectionEvaluator.IsPatternSelection)
            ? new[] { new CheckWarning(CheckWarningKind.InertTarget, "This rule is inert: its target selection matched no types.") }
            : NoWarnings;

        return (violations, warnings);
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

    // The throw verb (GRAMMAR §4.8, §5.3): a STRICT allow-list — every throw edge from a subject whose thrown
    // type is not in the allowed set is a violation, EXTERNAL thrown types included. This is OnlyReference
    // MINUS its external-target exemption: MustOnlyThrow constrains external throws too, so an unlisted
    // System.TimeoutException throw is red unless typeof(TimeoutException) is in the allow-set (a Type-sugar
    // operand resolves the external node by FQN, since the target universe includes externals). An allowed
    // type absent from the model resolves empty and harmlessly allows nothing. MustOnly* never warns — an
    // empty allow-set is loud by itself (the point of departure from ForbiddenCatch).
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) OnlyThrow(
        HashSet<TypeNode> subjects, IReadOnlyList<Selection> allowedThrows)
    {
        var allowed = ResolveOperands(allowedThrows);
        var violations = new List<Violation>();

        foreach (ThrowEdge edge in _throwEdges)
            if (subjects.Contains(edge.Source) && !allowed.Contains(edge.Thrown))
                violations.Add(Violation.Throw(edge.Source, edge.Thrown, edge.Sites));

        return (violations, NoWarnings);
    }

    // Every operand of a union subject must match at least one type (GRAMMAR §9): law must load
    // predictably, so a typo'd project name inside a four-way union fails the rule in its own right
    // instead of being silently absorbed by its siblings. One never-baselinable EmptySubject violation per
    // empty operand, in operand order; a non-union subject has no operands and passes straight through.
    // Target position keeps the softer per-rule inert-target warning instead.
    private IReadOnlyList<Violation> EmptySubjectOperands(Selection subject)
    {
        if (subject is not UnionSelection union) return Array.Empty<Violation>();

        var violations = new List<Violation>();
        foreach (Selection operand in union.Parts)
            if (_selections.Evaluate(operand, SelectionPosition.Subject).Count == 0)
                violations.Add(Violation.EmptySubject(EmptyOperandMessage(SentenceRenderer.Reference(operand))));

        return violations;
    }

    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) Shape(HashSet<TypeNode> subjects, Func<TypeNode, bool> holds)
    {
        var violations = new List<Violation>();
        foreach (TypeNode subject in subjects)
            if (!holds(subject))
                violations.Add(Violation.Shape(subject, subject.DeclarationSites));

        return (violations, NoWarnings);
    }

    // The negative hierarchy/attribute verbs (GRAMMAR §5.3, §5.7): a subject VIOLATES iff ANY anchor matches,
    // so it PASSES iff NONE do — the per-subject negation over the anchor list. The matchers are the same ones
    // backing the positives (SelectionEvaluator / MemberSelectionEvaluator), built once eagerly per anchor so
    // an unrepresentable anchor throws (→ RuleError) before any subject is tested, exactly as the positive
    // Shape arms do. Every anchor in every family is a TypeAnchor (typeof or definition name, GRAMMAR §5.2),
    // so the only axis left to be generic over is the subject kind: the same negation serves a type subject
    // and a member one, whose matchers differ in what they read rather than in what they are anchored on.
    private static Func<TSubject, bool> NoneOf<TSubject>(
        IReadOnlyList<TypeAnchor> anchors, Func<TypeAnchor, Func<TSubject, bool>> matcher)
    {
        var matchers = anchors.Select(matcher).ToList();
        return subject => !matchers.Any(match => match(subject));
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
            case MemberMustBeAttributedWithConstraint c:
                // The same matcher the member attribute ADJECTIVE narrows with (MemberSelectionEvaluator),
                // built once eagerly per anchor so an unrepresentable anchor throws (→ RuleError) before any
                // member is tested — the member twin of the type-side Shape arms.
                return MemberShape(members, MemberSelectionEvaluator.MemberAttributeMatcher(c.Anchor));
            case MemberMustNotBeAttributedWithConstraint c:
                return MemberShape(members, NoneOf(c.Anchors, MemberSelectionEvaluator.MemberAttributeMatcher));
            case MemberMustAcceptParameterConstraint c:
                // The anchor resolves through the SHARED definition-FQN path (SelectionEvaluator.DefinitionFullName,
                // the same helper .Returning's anchors resolve through) — so a method passes iff one declared
                // parameter's definition-level TypeFullName equals the anchor's (a non-generic anchor exactly, an
                // open-generic anchor on any construction, GRAMMAR §4.6). Resolving eagerly here is the
                // check-time closed-generic BACKSTOP: a constructed anchor throws RuleEvaluationException
                // (→ RuleError) before any member is tested. (.Returning's backstop fires a step earlier, inside
                // subject resolution — a divergence observable only for a validation-bypassed empty subject.)
                string parameterAnchor = SelectionEvaluator.DefinitionFullName(
                    c.ParameterType, "member parameter matching is definition-level. Anchor on the open definition instead.");
                return MemberShape(members, m => m.Parameters.Any(p => p.TypeFullName == parameterAnchor));
            case MemberMustConstraint c:
                return MemberShape(members, m => SelectionEvaluator.InvokePredicate(c.Predicate, m, "Must"));
            default:
                // Fail closed: as with the type-subject switch, an unhandled member verb is a missing
                // arm, not a pass — throw so it surfaces (contained per-rule by ArchChecker), never green.
                throw new InvalidOperationException($"Unhandled member constraint '{constraint.GetType().Name}'.");
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
