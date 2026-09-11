using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Fluent;
using Zphil.LoadBearing.Model;
using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Checking;

/// <summary>
///     How much of a rule's materialized subject set a generator emitted: <see cref="Types" /> is the whole
///     set, <see cref="Generated" /> the part of it no author wrote. A struct so that
///     <see langword="default" /> is the zero pair — what a rule that never reached a subject at all
///     (errored, skipped, empty) reports, with no null to thread through the checker.
/// </summary>
internal readonly struct SubjectCoverage
{
    public SubjectCoverage(int types, int generated)
    {
        Types = types;
        Generated = generated;
    }

    /// <summary>The size of the materialized subject set.</summary>
    public int Types { get; }

    /// <summary>How many of <see cref="Types" /> a generator emitted.</summary>
    public int Generated { get; }
}

/// <summary>
///     Evaluates one <see cref="Constraint" /> against the codebase, per-verb (GRAMMAR §4.1, §4.3,
///     §4.5, §4.7, §4.8, §4.9, §5.3): each dependency verb walks the <see cref="CodebaseModel" /> edge
///     list its family is extracted into, and each shape verb tests every subject directly.
/// </summary>
/// <remarks>
///     Every verb first requires a non-empty subject set — an empty subject fails the rule by default
///     (GRAMMAR §4.1). Violations come back unordered (the caller sorts), alongside any inert-target
///     warnings.
/// </remarks>
internal sealed class ConstraintEvaluator
{
    /// <summary>The pinned message on an empty-subject failure (ArchUnit precedent, GRAMMAR §4.1).</summary>
    internal const string EmptySubjectMessage = "The subject selection matched no solution-declared types.";

    /// <summary>The pinned message on an empty <em>member</em> subject failure (the member analog, GRAMMAR §4.6).</summary>
    internal const string EmptyMemberSubjectMessage = "The subject selection matched no solution-declared members.";

    /// <summary>The pinned message on an empty <em>project</em> subject failure (the project analog, GRAMMAR §4.10).</summary>
    internal const string EmptyProjectSubjectMessage = "The subject selection matched no solution-declared projects.";

    /// <summary>
    ///     The pinned warning text every forbidden-set verb raises on an inert target (GRAMMAR §4.1) —
    ///     one string for the whole family, so a correction to what "inert" means cannot land on some
    ///     verbs and miss others.
    /// </summary>
    private const string InertTargetMessage = "This rule is inert: its target selection matched no types.";

    private static readonly IReadOnlyList<CheckWarning> NoWarnings = Array.Empty<CheckWarning>();

    private readonly EdgeIndex<CatchEdge> _catchEdgesBySource;
    private readonly EdgeIndex<ConstructorEdge> _constructorEdgesBySource;
    private readonly EdgeIndex<ReferenceEdge> _edgesBySource;
    private readonly EdgeIndex<ReferenceEdge> _edgesByTarget;
    private readonly EdgeIndex<ExposureEdge> _exposureEdgesBySource;
    private readonly IncompleteModel? _incompleteModel;
    private readonly EdgeIndex<InjectionEdge> _injectionEdgesBySource;
    private readonly EdgeIndex<MemberEdge> _memberEdgesBySource;
    private readonly IReadOnlyList<ProjectNode> _projects;
    private readonly SelectionEvaluator _selections;
    private readonly EdgeIndex<ThrowEdge> _throwEdgesBySource;

    internal ConstraintEvaluator(CodebaseModel model, SelectionEvaluator selections, IncompleteModel? incompleteModel = null)
    {
        _incompleteModel = incompleteModel;
        _projects = model.Projects;
        _edgesBySource = new EdgeIndex<ReferenceEdge>(model.Edges, e => e.Source);
        _edgesByTarget = new EdgeIndex<ReferenceEdge>(model.Edges, e => e.Target);
        _memberEdgesBySource = new EdgeIndex<MemberEdge>(model.MemberEdges, e => e.Source);
        _constructorEdgesBySource = new EdgeIndex<ConstructorEdge>(model.ConstructorEdges, e => e.Source);
        _injectionEdgesBySource = new EdgeIndex<InjectionEdge>(model.InjectionEdges, e => e.Source);
        _catchEdgesBySource = new EdgeIndex<CatchEdge>(model.CatchEdges, e => e.Source);
        _throwEdgesBySource = new EdgeIndex<ThrowEdge>(model.ThrowEdges, e => e.Source);
        _exposureEdgesBySource = new EdgeIndex<ExposureEdge>(model.ExposureEdges, e => e.Source);
        _selections = selections;
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

    internal (IReadOnlyList<Violation> Violations, IReadOnlyList<CheckWarning> Warnings, SubjectCoverage Coverage)
        Evaluate(Constraint constraint)
    {
        // A project constraint ranges over the solution's PROJECTS (GRAMMAR §4.10), so it dispatches before
        // everything below: it has no type subject to resolve at all — Constraint.Subject is null for it —
        // and its own empty check speaks in project terms.
        if (constraint is ProjectConstraint projectConstraint) return EvaluateProject(projectConstraint);

        // Loud per-operand emptiness for a union subject (GRAMMAR §9), ahead of the member dispatch because
        // MemberConstraint.Subject IS the underlying type selection — so one gate covers the type- and
        // member-subject paths alike. The gate hands back the admission it folded the operands into, so
        // the union subject is neither evaluated a second time nor attributed a second time. The bang is the
        // project dispatch above: every constraint that reaches here carries a type selection.
        (IReadOnlyList<Violation> emptyOperands, SelectionAdmission? resolved, FamilySubject? family) =
            ResolveSubject(constraint.Subject!);
        if (resolved is not { } admission) return (emptyOperands, NoWarnings, default);

        // A member-subject constraint (GRAMMAR §4.6) ranges over declared members, so it dispatches before
        // the type-subject gate: its own empty check speaks in member terms (a type subject that matches
        // types none of whose members survive the kind filter is the ordinary way to fail empty).
        if (constraint is MemberConstraint memberConstraint) return EvaluateMember(memberConstraint, admission.Members);

        HashSet<TypeNode> subjects = admission.Members;
        if (subjects.Count == 0)
            return (
            [
                Violation.EmptySubject(
                    EmptySubjectMessage, AuthoringHints.ForSubject(constraint.Subject!, _incompleteModel))
            ], NoWarnings, default);

        (IReadOnlyList<Violation> violations, IReadOnlyList<CheckWarning> warnings) =
            Dispatch(constraint, subjects, admission, family);
        return (violations, warnings, CoverageOf(subjects));
    }

    // The subject-coverage pair the report states (GRAMMAR §5.2): how big the materialized subject set is
    // and how much of it a generator emitted. Measured HERE, on the set the verbs actually range over,
    // rather than on the selection that produced it — which is what makes the statement self-extinguishing:
    // adding .Authored() leaves nothing generated, and the statement disappears with no separate rule about
    // when to suppress it. Both numbers, because "803 of 804" and "803 of 90,000" are different findings and
    // the denominator appears nowhere else in the document.
    private static SubjectCoverage CoverageOf(IReadOnlyCollection<TypeNode> subjects)
    {
        return new SubjectCoverage(subjects.Count, subjects.Count(type => type.IsGenerated));
    }

    // A member subject's coverage is reported in DECLARING TYPES, not members: the pair has to keep one unit
    // for "n of m" to mean anything, and the generated-ness fact is a type fact — a member of a generated
    // type is generated code whether or not it is counted as one.
    private static SubjectCoverage MemberCoverageOf(IReadOnlyList<MemberNode> members)
    {
        var declaringTypes = new HashSet<ITypeInfo>(members.Select(member => member.DeclaringType));
        return new SubjectCoverage(declaringTypes.Count, declaringTypes.Count(type => type.IsGenerated));
    }

    // The verb dispatch, lifted out of Evaluate unchanged so the coverage pair can ride beside the pair every
    // arm returns. No arm knows about coverage — the subject set it is measured from is already resolved.
    // Every EDGE verb reads the admission rather than the bare set: the subject bounds which of an edge's
    // per-declarer instances the rule owns, at whichever end it sits (§4.1). The shape verbs and the member
    // path take the set, because a rule with no edge has nothing to attribute.
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) Dispatch(
        Constraint constraint, HashSet<TypeNode> subjects, SelectionAdmission admission, FamilySubject? family)
    {
        switch (constraint)
        {
            case MustNotReferenceConstraint c:
                return ForbiddenReference(admission, c.Targets, inbound: false);
            case MustNotBeReferencedByConstraint c:
                return ForbiddenReference(admission, c.Sources, inbound: true);
            case MustOnlyReferenceConstraint or MustOnlyReferenceItselfConstraint:
                // The leaf verb rides the allow-list's own arm with the empty operand list it was built to
                // carry: an empty admission, which the implicit self-allowance folds back to the subject alone.
                return OnlyReference(admission, constraint.Operands, family);
            case MustOnlyBeReferencedByConstraint or MustOnlyBeReferencedByItselfConstraint:
                // The inbound pair, likewise: the leaf's empty operand list resolves to the subject alone,
                // and MustOnlyBeReferencedBy's Sources IS the inherited operand list.
                return OnlyBeReferencedBy(admission, constraint.Operands, family);
            case MustNotReferenceEachOtherConstraint:
                return EachOther(family);
            case MustNotHaveCircularReferencesConstraint:
                return CircularReferences(family);
            case MustNotUseConstraint c:
                return ForbiddenMemberUse(subjects, c.Members);
            case MustNotConstructConstraint c:
                return ForbiddenConstruction(admission, c.Targets);
            case MustNotInjectConstraint c:
                return ForbiddenInjection(admission, c.Targets);
            case MustNotCatchConstraint c:
                return ForbiddenCatch(admission, c.Targets);
            case MustNotCatchUnfilteredConstraint c:
                return ForbiddenUnfilteredCatch(admission, c.Targets);
            case MustNotSwallowConstraint c:
                return ForbiddenSwallow(admission, c.Targets);
            case MustNotExposeConstraint c:
                return ForbiddenExposure(admission, c.Targets);
            case MustOnlyThrowConstraint c:
                return OnlyThrow(admission, c.Targets);
            case MustNotThrowConstraint c:
                return ForbiddenThrow(admission, c.Targets);
            case MustResideInNamespaceConstraint c:
                var namespacePattern = new NamespacePattern(c.Glob);
                return Shape(subjects, t => namespacePattern.Matches(t.Namespace));
            case MustResideInProjectConstraint c:
                // The N-way declarer predicate, never ProjectName equality: one source file compiled into
                // several projects resides in every one of them (GRAMMAR §4.1).
                return Shape(subjects, t => t.IsDeclaredBy(c.ProjectName));
            case MustBelongToConstraint c:
                // Memberships resolve in SUBJECT position — they name where a subject may live, not an
                // edge's far end — and the test is plain membership, because there is no edge to attribute.
                SelectionAdmission membership =
                    SelectionAdmission.Operands(_selections, c.Memberships, SelectionPosition.Subject);
                return Shape(subjects, membership.Contains);
            case MustHaveExactlyOneCounterpartConstraint c:
                return Counterparts(subjects, c);
            case MustBeRegisteredConstraint:
                HashSet<TypeNode> registered = _selections.RegisteredMembers(SelectionPosition.Subject);
                return Shape(subjects, registered.Contains);
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

    /// <summary>
    ///     The one walk behind every forbidden-set verb (GRAMMAR §4.1, §4.3, §4.5, §4.7, §4.8, §4.9):
    ///     resolve the operands, keep each candidate edge that counts against the rule, mint one violation
    ///     per survivor from the sites that verb treats as evidence, and — for
    ///     the arms that warn — raise the inert-target warning. Inert only when the forbidden operand set
    ///     is empty AND at least one operand is a pattern selection; a bare <c>typeof</c> target absent
    ///     from the codebase is the win condition, not a warning. <c>requireSites</c> is the refinement
    ///     the two catch subsets carry: an edge none of whose sites the ban forbids is green, and no
    ///     printed <c>file:line</c> is ever a site the ban permits.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <paramref name="subject" /> rides beside the candidates because the §4.1 edge rule needs
    ///         both ends of the same test: the subject bounds which of the edge's per-declarer instances
    ///         the rule owns, and the operand set decides the far end at each of them. Which end the
    ///         subject sits at is <paramref name="subjectAtSource" /> — true for every verb here but the
    ///         inbound reference one.
    ///     </para>
    ///     <para>
    ///         The live trap for that one arm: its operand admission is resolved in
    ///         <see cref="SelectionPosition.Target" /> and then plays the edge's SOURCE role. Benign,
    ///         because target position differs only by admitting external nodes and an edge source is
    ///         always solution-declared, but it is no longer self-evident from the call.
    ///     </para>
    /// </remarks>
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) ForbiddenEdge<TEdge>(
        IEnumerable<TEdge> candidates,
        SelectionAdmission subject,
        IReadOnlyList<Selection> operands,
        Func<TEdge, TypeNode> sourceOf,
        Func<TEdge, TypeNode> targetOf,
        Func<TEdge, IReadOnlyList<SourceLocation>> sitesOf,
        Func<TEdge, IReadOnlyList<SourceLocation>, Violation> toViolation,
        bool subjectAtSource,
        bool requireSites,
        bool warnInert)
    {
        SelectionAdmission operandSet = ResolveOperands(operands);
        var violations = new List<Violation>();

        foreach (TEdge edge in candidates)
        {
            bool forbidden = SelectionAdmission.CountsEdge(
                subject, operandSet, sourceOf(edge), targetOf(edge), subjectAtSource, wantHit: true);

            if (!forbidden) continue;

            IReadOnlyList<SourceLocation>? sites = sitesOf(edge);
            if (requireSites && sites.Count == 0) continue;

            violations.Add(toViolation(edge, sites));
        }

        IReadOnlyList<CheckWarning> warnings = warnInert
                                               && violations.Count == 0
                                               && operandSet.Count == 0
                                               && operands.Any(SelectionEvaluator.IsPatternSelection)
            ?
            [
                new CheckWarning(CheckWarningKind.InertTarget, InertTargetMessage,
                    hint: AuthoringHints.ForInertTarget(operands, _incompleteModel))
            ]
            : NoWarnings;

        return (violations, warnings);
    }

    // The candidate edges of one kind for a subject-keyed walk: only those whose keyed endpoint is a
    // subject, read off the per-kind index rather than by re-walking the whole edge list (the reference
    // list is the biggest the extractor produces, and every rule paid O(|edges|) for it). Each edge sits
    // under exactly one key and `subjects` is a set, so no edge is yielded twice. Walk order becomes the
    // subject set's rather than the model's, which is unobservable: ArchChecker re-sorts every violation
    // list on (source, target, member) before it reaches a report, and edges are unique per endpoint
    // pair, so no tie-break rides on the walk.
    private static IEnumerable<TEdge> Keyed<TEdge>(HashSet<TypeNode> subjects, ILookup<TypeNode, TEdge> index)
    {
        return subjects.SelectMany(subject => index[subject]);
    }

    /// <summary>
    ///     The reference verbs (GRAMMAR §4.3). Outbound (<c>MustNotReference</c>): edge subject→operand.
    ///     Inbound (<c>MustNotBeReferencedBy</c>): edge operand→subject, so the walk keys on the target
    ///     while the violation still names <c>Source</c> — the referencing type, where the edit happens.
    /// </summary>
    /// <remarks>
    ///     The two directions are one test read from opposite ends (§4.1), which is why neither filters
    ///     the candidates first: "some instance the subject owns puts the operand at the far end" is a
    ///     claim about a declarer both ends agree on, and two independent filters cannot state it.
    ///     Inbound, the subject sits at the target and bounds ownership there while the operand decides
    ///     the source; outbound, the other way round.
    /// </remarks>
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) ForbiddenReference(
        SelectionAdmission subject, IReadOnlyList<Selection> operands, bool inbound)
    {
        ILookup<TypeNode, ReferenceEdge> index = inbound ? _edgesByTarget.Lookup : _edgesBySource.Lookup;

        return ForbiddenEdge(
            Keyed(subject.Members, index), subject, operands, e => e.Source, e => e.Target, e => e.Sites,
            (e, sites) => Violation.Reference(e.Source, e.Target, sites),
            subjectAtSource: !inbound, requireSites: false, warnInert: true);
    }

    // The member-access verb (GRAMMAR §4.5): a member edge is a hit when its source is a subject AND
    // its used member matches a banned (declaring type, name) pair — ordinal, one ban covering every
    // overload. Per-overload edges yield per-overload MemberUse violations (the §4.3 identity substrate).
    // The banned set is resolved eagerly so a closed-generic member anchor is refused (RuleError) before
    // any edge is tested — mirroring the type-noun refusal in SelectionEvaluator.DefinitionFullName.
    // Keyed on names rather than on nodes, the ban is attribution-insensitive by construction, and it must
    // stay that way: a (declaring type, name) string set has no head to read a project off, so there is no
    // instance for it to be asked about. That is also the answer §4.1 already gives a typeof operand — a
    // member of a type several projects compile is banned in every one of them, its own copy included.
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
        foreach (MemberEdge edge in Keyed(subjects, _memberEdgesBySource.Lookup))
            if (banned.Contains((edge.Member.ContainingType.FullName, edge.Member.Name)))
                violations.Add(Violation.MemberUse(edge.Source, edge.Member, edge.Sites));

        // MustNotUse never warns: member targets are concrete (type, name) anchors (no pattern form), so a banned member
        // absent from the codebase is the win condition, exactly like a bare typeof target (GRAMMAR §4.5).
        return (violations, NoWarnings);
    }

    /// <summary>
    ///     The construction verb (GRAMMAR §4.5, §5.3): a construction edge is a hit when its source is a
    ///     subject AND the constructed type is a forbidden operand — the "you may use it; you may not
    ///     create it" ban. Extraction already collapses every <c>new</c> of one type into one (source,
    ///     constructed) edge with its sites aggregated, so one edge yields one Construction violation
    ///     keyed on the type pair (overload-indifferent, §4.3).
    /// </summary>
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) ForbiddenConstruction(
        SelectionAdmission subject, IReadOnlyList<Selection> operands)
    {
        return ForbiddenEdge(
            Keyed(subject.Members, _constructorEdgesBySource.Lookup), subject, operands,
            e => e.Source, e => e.Constructed, e => e.Sites,
            (e, sites) => Violation.Construction(e.Source, e.Constructed, sites),
            subjectAtSource: true, requireSites: false, warnInert: true);
    }

    /// <summary>
    ///     The injection verb (GRAMMAR §4.7, §5.3): an injection edge is a hit when its source is a
    ///     subject AND the injected parameter type is a forbidden operand — the captive-dependency ban.
    ///     Extraction already collapses every constructor parameter typed on one injected type into one
    ///     (source, injected) edge with its sites aggregated, so one edge yields one Injection violation
    ///     keyed on the type pair (constructor-overload- and parameter-name-indifferent, §4.3). Unlike
    ///     ForbiddenConstruction, <c>MustNotInject</c> NEVER warns — hence <c>warnInert: false</c>: its
    ///     natural operand is a Registered selection (§4.7), and an empty Registered operand means no such
    ///     registrations exist — the win condition, exactly like a bare <c>typeof</c> target (GRAMMAR
    ///     §4.1). An empty operand set is silence, not a warning.
    /// </summary>
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) ForbiddenInjection(
        SelectionAdmission subject, IReadOnlyList<Selection> operands)
    {
        return ForbiddenEdge(
            Keyed(subject.Members, _injectionEdgesBySource.Lookup), subject, operands,
            e => e.Source, e => e.Injected, e => e.Sites,
            (e, sites) => Violation.Injection(e.Source, e.Injected, sites),
            subjectAtSource: true, requireSites: false, warnInert: false);
    }

    /// <summary>
    ///     The catch verb (GRAMMAR §4.8, §5.3): a catch edge is a hit when its source is a subject AND the
    ///     caught type is a forbidden operand — the "you may throw it; you may not swallow it" ban.
    ///     Matching is exact definition-level FQN on the operand set (the family's shared node
    ///     membership): <c>MustNotCatch(typeof(Exception))</c> flags only <c>catch (System.Exception)</c>
    ///     and bare-catch edges (a bare catch already synthesized System.Exception at extraction), never a
    ///     narrower <c>catch (IOException)</c>.
    /// </summary>
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) ForbiddenCatch(
        SelectionAdmission subject, IReadOnlyList<Selection> operands)
    {
        return ForbiddenEdge(
            Keyed(subject.Members, _catchEdgesBySource.Lookup), subject, operands,
            e => e.Source, e => e.Caught, e => e.Sites,
            (e, sites) => Violation.Catch(e.Source, e.Caught, sites),
            subjectAtSource: true, requireSites: false, warnInert: true);
    }

    /// <summary>
    ///     The filter-aware catch verb (GRAMMAR §4.8, §5.3): the same walk as ForbiddenCatch over the same
    ///     edges, decided on the edge's recorded UNFILTERED sites instead of its sites. A matching edge
    ///     violates iff at least one of its <c>catch</c> clauses spells no <c>when</c> filter, and the
    ///     evidence is exactly those sites — so an edge every one of whose sites is filtered is GREEN, and
    ///     no printed file:line is ever a filtered site. Extraction records the unfiltered subset as its
    ///     own fact (§4.8), so this arm reads it directly as evidence rather than subtracting one set from
    ///     another (the polarity that keeps a same-line collision red-biased). Identity is untouched — the
    ///     (source, caught) type pair, the very key ForbiddenCatch's violations carry, so the unfiltered
    ///     sites are evidence and never identity (§4.3) and a baseline entry means the same thing under
    ///     either catch verb. Matching stays exact definition-level FQN and the inert-target warning is the
    ///     §4.1 forbidden-set family's, both exactly as ForbiddenCatch.
    /// </summary>
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) ForbiddenUnfilteredCatch(
        SelectionAdmission subject, IReadOnlyList<Selection> operands)
    {
        return ForbiddenEdge(
            Keyed(subject.Members, _catchEdgesBySource.Lookup), subject, operands,
            e => e.Source, e => e.Caught, e => e.UnfilteredSites,
            (e, sites) => Violation.Catch(e.Source, e.Caught, sites),
            subjectAtSource: true, requireSites: true, warnInert: true);
    }

    /// <summary>
    ///     The rethrow-aware catch verb (GRAMMAR §4.8, §5.3): the same walk again over the same edges,
    ///     decided on the edge's recorded SWALLOWING sites — the ones that are unfiltered AND do not end in
    ///     a throw. A matching edge violates iff at least one of its clauses holds the failure and
    ///     continues, and the evidence is exactly those sites, so an edge whose every unfiltered clause
    ///     rethrows or translates is GREEN and no printed file:line is ever a rethrowing site. Extraction
    ///     records this subset as its own fact for the same polarity reason as the unfiltered subset
    ///     (§4.8) — read the sites the ban forbids, never a complement. Identity is untouched: the
    ///     (source, caught) type pair, so a baseline entry means the same thing under all three catch
    ///     verbs, the sites stay evidence (§4.3), and narrowing which sites a violation prints never
    ///     narrows what it keys. Matching stays exact definition-level FQN and the inert-target warning is
    ///     the §4.1 forbidden-set family's.
    /// </summary>
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) ForbiddenSwallow(
        SelectionAdmission subject, IReadOnlyList<Selection> operands)
    {
        return ForbiddenEdge(
            Keyed(subject.Members, _catchEdgesBySource.Lookup), subject, operands,
            e => e.Source, e => e.Caught, e => e.SwallowingSites,
            (e, sites) => Violation.Catch(e.Source, e.Caught, sites),
            subjectAtSource: true, requireSites: true, warnInert: true);
    }

    /// <summary>
    ///     The throw-ban verb (GRAMMAR §4.8, §5.3): the ban polarity beside MustOnlyThrow's strict
    ///     allow-list — a throw edge is a hit when its source is a subject AND the thrown type is a
    ///     forbidden operand, for the case where the forbidden thrown types are enumerable and the
    ///     permitted ones are not. Matching is exact definition-level FQN on the operand set, so a ban on
    ///     Exception never reaches a derived throw (the narrow throw is the good state, mirroring the catch
    ///     verbs). Inert-target warning semantics are the §4.1 forbidden-set family's, exactly as
    ///     ForbiddenCatch — the point of departure from OnlyThrow, which never warns because an empty
    ///     allow-set is loud on its own.
    /// </summary>
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) ForbiddenThrow(
        SelectionAdmission subject, IReadOnlyList<Selection> operands)
    {
        return ForbiddenEdge(
            Keyed(subject.Members, _throwEdgesBySource.Lookup), subject, operands,
            e => e.Source, e => e.Thrown, e => e.Sites,
            (e, sites) => Violation.Throw(e.Source, e.Thrown, sites),
            subjectAtSource: true, requireSites: false, warnInert: true);
    }

    /// <summary>
    ///     The exposure verb (GRAMMAR §4.9, §5.3): an exposure edge is a hit when its source is a subject
    ///     AND the exposed type is a forbidden operand — the "you may use it; you may not surface it on
    ///     your public API" ban. Matching is exact definition-level FQN on the operand set (the family's
    ///     shared node membership): <c>MustNotExpose(typeof(DataTable))</c> flags only a <c>DataTable</c>
    ///     signature position, never a narrower <c>DataView</c> one (no hierarchy-aware matching).
    /// </summary>
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) ForbiddenExposure(
        SelectionAdmission subject, IReadOnlyList<Selection> operands)
    {
        return ForbiddenEdge(
            Keyed(subject.Members, _exposureEdgesBySource.Lookup), subject, operands,
            e => e.Source, e => e.Exposed, e => e.Sites,
            (e, sites) => Violation.Expose(e.Source, e.Exposed, sites),
            subjectAtSource: true, requireSites: false, warnInert: true);
    }

    /// <summary>
    ///     The outbound allow-list and its leaf form (GRAMMAR §4.1, §5.3): every reference out of the
    ///     subject whose target the allow-set does not name is a violation.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The refined subject is allowed implicitly, as one more allow entry (GRAMMAR §4.1) — folded
    ///         into the resolved admission here rather than into the Targets list, so Operands, the
    ///         sentence and the diagram never see an entry no author wrote. External targets are exempt
    ///         (the complement universe is solution-declared). <c>MustOnly*</c> never warns — an empty
    ///         allow-set is loud by itself, and the leaf verb's empty operand list resolves to
    ///         subject-only rather than to nothing. Attribution survives by flipping the polarity
    ///         <see cref="SelectionAdmission.CountsEdge" /> asks with: an edge is a violation when SOME
    ///         instance the subject owns lands outside the allow-set. So an intra-copy edge is allowed by
    ///         an entry naming the compiling project — the subject counts as one, at the projects it names
    ///         the node at — or by an entry that is not a project at all, and never by an entry naming some
    ///         other declarer of the same source file.
    ///     </para>
    ///     <para>
    ///         Over a family subject "self" is the cell rather than the whole subject (GRAMMAR §5.1), so
    ///         the walk runs once per cell: the subject is that cell intersected with the family's
    ///         membership, and the allow-set is the operands plus the same cell <em>as declared</em> — the
    ///         whole layer or project, which is what lets a module's excluded <c>Contracts</c> cone still
    ///         count as its own.
    ///     </para>
    /// </remarks>
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) OnlyReference(
        SelectionAdmission subject, IReadOnlyList<Selection> allowedTargets, FamilySubject? family)
    {
        return AllowList(
            subject, allowedTargets, family,
            (cell, allowed, label) => Outbound(cell, allowed, wantHit: false, label));
    }

    /// <summary>
    ///     The inbound allow-list and its leaf form: any reference into the subject from outside the
    ///     allow-set is a violation (the containment verb, GRAMMAR §7), the refined subject being one of
    ///     the allowed sources implicitly (§4.1, as for the outbound verb).
    /// </summary>
    /// <remarks>
    ///     Edge sources are always solution-declared, so no external caveat is needed. The subject sits at
    ///     the edge's TARGET end, so it bounds ownership there while the allow-set decides the source — one
    ///     test rather than two, because "some instance the subject owns comes from an unallowed project"
    ///     is a claim about one declarer and cannot be split across independent filters (§4.1). The family
    ///     path is the outbound one read from the other end: per cell, allowed = the operands plus that
    ///     cell as declared.
    /// </remarks>
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) OnlyBeReferencedBy(
        SelectionAdmission subject, IReadOnlyList<Selection> allowedSources, FamilySubject? family)
    {
        return AllowList(subject, allowedSources, family, Inbound);
    }

    // The allow-list shape both directions take (GRAMMAR §4.1, §5.1): the operands resolved once, the
    // subject allowed implicitly, and — over a family — one walk per cell whose allow-set is the operands
    // plus that cell as declared. Only the walk differs, which is what `walk` carries; its third argument is
    // the cell each violation is labelled with, and it is the LOOP index rather than anything read off the
    // edge, so the label is the cell whose law was broken at either end. That distinction is real for the
    // inbound direction alone: there the subject sits at the edge's target, so the referencing type is
    // regularly outside the family altogether and has no cell to be attributed to.
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) AllowList(
        SelectionAdmission subject, IReadOnlyList<Selection> allowed, FamilySubject? family,
        Func<SelectionAdmission, SelectionAdmission, string?, List<Violation>> walk)
    {
        SelectionAdmission operands = ResolveOperands(allowed);
        if (family is null) return (walk(subject, operands.IncludingSelf(subject), null), NoWarnings);

        IReadOnlyList<SelectionAdmission> declared = DeclaredCells(family);
        var violations = new List<Violation>();
        for (var cell = 0; cell < declared.Count; cell++)
            violations.AddRange(
                walk(family.Subjects[cell], operands.IncludingSelf(declared[cell]), family.CellName(cell)));

        return (Deduplicated(violations), NoWarnings);
    }

    /// <summary>
    ///     The cross-cell ban (GRAMMAR §5.1, §5.3): per cell, an owned outbound instance whose far end
    ///     lies in any <em>other</em> cell, as declared, is a violation. Symmetric, so one outbound walk
    ///     states the whole law, and a one-cell family passes vacuously.
    /// </summary>
    /// <remarks>
    ///     The polarity is the allow-list's flipped — <c>wantHit: true</c> over the merged other cells —
    ///     which is what keeps one edge test behind every verb at either end (§4.1). A family subject is
    ///     required by spec-build item 28, so the null arm fails closed rather than describing a shape a
    ///     built model can carry.
    /// </remarks>
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) EachOther(FamilySubject? family)
    {
        if (family is null)
            throw new RuleEvaluationException(
                "`MustNotReferenceEachOther` needs a family subject (`arch.Each`); this subject declares no cells.");

        IReadOnlyList<SelectionAdmission> declared = DeclaredCells(family);
        var violations = new List<Violation>();
        for (var cell = 0; cell < declared.Count; cell++)
            violations.AddRange(
                Outbound(
                    family.Subjects[cell], SelectionAdmission.AllBut(declared, cell), wantHit: true,
                    family.CellName(cell)));

        return (Deduplicated(violations), NoWarnings);
    }

    /// <summary>
    ///     The cycle gate (GRAMMAR §5.1, §5.3): an arrow runs from cell to cell wherever some owned
    ///     reference crosses between them, the strongly connected components are taken over those arrows,
    ///     and every type pair on an arrow whose two cells share a component is a violation. A one-cell
    ///     family and any acyclic cell graph pass; an arrow into a component from outside it is green,
    ///     because it lies on no circle.
    /// </summary>
    /// <remarks>
    ///     A violation's identity is the type pair, never the cycle: a baseline keyed on circles would churn
    ///     as arrows close new circles and enumerating a component's circles is exponential, so the pair is
    ///     what keeps the baseline, the ratchet, the human report and SARIF untouched by this verb. The
    ///     consequence is that the intended direction is blamed beside the stray back-reference — both
    ///     arrows of a two-cell circle red — by design: this verb says only that a circle exists, and the
    ///     author who knows which way the cells should point writes an ordering rule instead. The circle's
    ///     cell names ride on <see cref="Violation.Detail" />, which the JSON channel alone reads.
    ///     <para>
    ///         A family of layers is required by spec-build item 29, so both throws below fail closed
    ///         rather than describing a shape a built model can carry — the projects arm doubly so, since
    ///         the build forbids circular project references and the law could never red.
    ///     </para>
    /// </remarks>
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) CircularReferences(FamilySubject? family)
    {
        if (family is null)
            throw new RuleEvaluationException(
                "`MustNotHaveCircularReferences` needs a family of layers (`arch.Each`); this subject declares no cells.");

        if (family.LayerCells is not { } layerCells)
            throw new RuleEvaluationException(
                "`MustNotHaveCircularReferences` needs a family of layers (`arch.Each`); this subject's cells are projects, which cannot have circular references.");

        IReadOnlyList<SelectionAdmission> declared = DeclaredCells(family);
        int cellCount = declared.Count;

        // The cell graph, materialized as the type pairs realizing each arrow: arrows[i, j] is what a
        // reference from cell i into cell j is made of, and its emptiness is the absence of the arrow.
        var arrows = new List<ReferenceEdge>[cellCount, cellCount];
        for (var source = 0; source < cellCount; source++)
        for (var target = 0; target < cellCount; target++)
            arrows[source, target] = source == target
                ? new List<ReferenceEdge>()
                : OutboundEdges(family.Subjects[source], declared[target], wantHit: true);

        int[] components = CellComponents.Of(cellCount, (source, target) => arrows[source, target].Count > 0);
        Dictionary<int, string> circles = CircleDetails(layerCells, components);

        var violations = new List<Violation>();
        for (var source = 0; source < cellCount; source++)
        for (var target = 0; target < cellCount; target++)
        {
            List<ReferenceEdge> arrow = arrows[source, target];
            if (arrow.Count == 0 || components[source] != components[target]) continue;

            string detail = circles[components[source]];
            string cell = family.CellName(source);
            foreach (ReferenceEdge edge in arrow)
                violations.Add(Violation.Reference(edge.Source, edge.Target, edge.Sites, detail).InCell(cell));
        }

        return (Deduplicated(violations), NoWarnings);
    }

    // One detail sentence per component that holds a circle, naming its cells in family DECLARATION order —
    // the order the rule's own sentence names them in, so the finding and the law read alike. A component of
    // one cell holds no circle and mints nothing, which is why the caller skips an empty arrow before
    // reaching in here: a cell IS in its own component, and arrows[i, i] is empty by construction.
    private static Dictionary<int, string> CircleDetails(IReadOnlyList<Layer> cells, int[] components)
    {
        var names = new Dictionary<int, List<string>>();
        for (var cell = 0; cell < components.Length; cell++)
        {
            int component = components[cell];
            if (!names.TryGetValue(component, out List<string>? members))
            {
                members = new List<string>();
                names[component] = members;
            }

            members.Add(cells[cell].Name);
        }

        return names
            .Where(component => component.Value.Count > 1)
            .ToDictionary(
                component => component.Key,
                component => $"circular references among the {ProseFormat.JoinReferencesAnd(component.Value)} layers");
    }

    // The outbound reference walk the allow-list, the leaf and the cross-cell ban share, parameterized by
    // the polarity each asks with. External targets are exempt either way: the complement universe is
    // solution-declared, and a cell never holds an external type to be an "other" of.
    private List<Violation> Outbound(
        SelectionAdmission subject, SelectionAdmission operand, bool wantHit, string? cell = null)
    {
        return OutboundEdges(subject, operand, wantHit)
            .Select(edge => Violation.Reference(edge.Source, edge.Target, edge.Sites)
                .InCell(cell))
            .ToList();
    }

    // The walk itself, kept apart from the mint because the cycle gate needs the EDGES of an arrow before
    // it knows whether that arrow lies on a circle — and only then whether they are violations at all.
    private List<ReferenceEdge> OutboundEdges(SelectionAdmission subject, SelectionAdmission operand, bool wantHit)
    {
        var edges = new List<ReferenceEdge>();
        foreach (ReferenceEdge edge in Keyed(subject.Members, _edgesBySource.Lookup))
            if (!edge.Target.IsExternal
                && SelectionAdmission.CountsEdge(
                    subject, operand, edge.Source, edge.Target, subjectAtSource: true, wantHit))
                edges.Add(edge);

        return edges;
    }

    // The inbound twin, keyed on the edge's target so the subject bounds ownership where it sits.
    private List<Violation> Inbound(SelectionAdmission subject, SelectionAdmission allowed, string? cell = null)
    {
        var violations = new List<Violation>();
        foreach (ReferenceEdge edge in Keyed(subject.Members, _edgesByTarget.Lookup))
            if (SelectionAdmission.CountsEdge(
                    subject, allowed, edge.Source, edge.Target, subjectAtSource: false, wantHit: false))
                violations.Add(Violation.Reference(edge.Source, edge.Target, edge.Sites)
                    .InCell(cell));

        return violations;
    }

    // A family's cells collected in TARGET position — each cell as declared, which is what "self" means on
    // a family (GRAMMAR §5.1). Resolved here rather than in ResolveSubject so a family rule whose verb
    // never reads the partition pays nothing for it.
    private IReadOnlyList<SelectionAdmission> DeclaredCells(FamilySubject family)
    {
        return family.Cells
            .Select(cell => SelectionAdmission.Collect(_selections, cell, SelectionPosition.Target))
            .ToList();
    }

    // One violation per (source, target) pair however many cells minted it (GRAMMAR §4.3): a project
    // family's cells overlap wherever one source file compiles into two of them, so the same edge can be
    // judged twice while the baseline still keys on the pair. Keeping the FIRST of a duplicated pair is
    // also what settles which cell such an edge is attributed to — the first in declaration order, the
    // walk being in that order — so the burndown's per-cell split names one cell per pair and totals to
    // the rule's own count.
    private static IReadOnlyList<Violation> Deduplicated(List<Violation> violations)
    {
        if (violations.Count < 2) return violations;

        var seen = new HashSet<(TypeNode Source, TypeNode Target)>();
        var unique = new List<Violation>(violations.Count);
        foreach (Violation violation in violations)
            if (seen.Add((violation.Source!, violation.Target!)))
                unique.Add(violation);

        return unique;
    }

    // The throw verb (GRAMMAR §4.8, §5.3): a STRICT allow-list — every throw edge from a subject whose thrown
    // type is not in the allowed set is a violation, EXTERNAL thrown types included. This is OnlyReference
    // MINUS its external-target exemption: MustOnlyThrow constrains external throws too, so an unlisted
    // System.TimeoutException throw is red unless typeof(TimeoutException) is in the allow-set (a Type-sugar
    // operand resolves the external node by FQN, since the target universe includes externals). An allowed
    // type absent from the model resolves empty and harmlessly allows nothing. MustOnly* never warns — an
    // empty allow-set is loud by itself (the point of departure from ForbiddenCatch). The allow-set is a
    // target position like any other, so §4.1 attribution decides membership here too: a project-headed
    // entry allows a throw of a type several projects compile only at the instance whose throwing
    // compilation reached that project's copy.
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) OnlyThrow(
        SelectionAdmission subject, IReadOnlyList<Selection> allowedThrows)
    {
        SelectionAdmission allowed = ResolveOperands(allowedThrows);
        var violations = new List<Violation>();

        foreach (ThrowEdge edge in Keyed(subject.Members, _throwEdgesBySource.Lookup))
            if (SelectionAdmission.CountsEdge(
                    subject, allowed, edge.Source, edge.Thrown, subjectAtSource: true, wantHit: false))
                violations.Add(Violation.Throw(edge.Source, edge.Thrown, edge.Sites));

        return (violations, NoWarnings);
    }

    // The subject, resolved once: its membership, the heads that admitted each conflated node in it, and
    // the §9 per-operand emptiness verdict a union subject carries. Law must load predictably, so a typo'd
    // project name inside a four-way union fails the rule in its own right instead of being silently
    // absorbed by its siblings — one never-baselinable EmptySubject violation per empty operand, in
    // operand order, and no admission at all, because a rule that fails this gate never reaches a verb and
    // must never have the union's own adjectives applied. Target position keeps the softer per-rule
    // inert-target warning instead. A non-union subject has no operands and resolves straight through; a
    // union's operands ARE the union, so collecting them here is what keeps it from being resolved twice.
    private (IReadOnlyList<Violation> Empty, SelectionAdmission? Subject, FamilySubject? Family) ResolveSubject(
        Selection subject)
    {
        if (subject is UnionSelection union) return ResolveUnionSubject(union);
        if (subject.Noun is EachNoun family) return ResolveFamilySubject(subject, family);

        SelectionAdmission whole = SelectionAdmission.Collect(_selections, subject, SelectionPosition.Subject);
        return (Array.Empty<Violation>(), whole, null);
    }

    private (IReadOnlyList<Violation>, SelectionAdmission?, FamilySubject?) ResolveUnionSubject(UnionSelection union)
    {
        (List<SelectionAdmission> operands, List<Violation> violations) = CollectSubjectParts(union.Parts);
        if (violations.Count > 0) return (violations, null, null);

        return (Array.Empty<Violation>(), SelectionAdmission.Folded(_selections, union, operands), null);
    }

    // Each part collected in subject position with the §9 emptiness verdict beside it, in part order: a
    // union's operands and a family's cells are loud for the same reason and must report alike. Each is
    // cured on its own terms, though — the parts of one union need not share a shape.
    private (List<SelectionAdmission> Collected, List<Violation> Empty) CollectSubjectParts(
        IReadOnlyList<Selection> parts)
    {
        var collected = new List<SelectionAdmission>(parts.Count);
        var violations = new List<Violation>();
        foreach (Selection part in parts)
        {
            SelectionAdmission matched = SelectionAdmission.Collect(_selections, part, SelectionPosition.Subject);
            collected.Add(matched);
            if (matched.Count == 0)
                violations.Add(Violation.EmptySubject(
                    EmptyOperandMessage(SentenceRenderer.Reference(part)),
                    AuthoringHints.ForSubject(part, _incompleteModel)));
        }

        return (collected, violations);
    }

    // A family subject, resolved once for the whole rule (GRAMMAR §5.1): its cells, their memberships, and
    // the partition discipline. Overlap is fail-closed rather than silently double-judged: a type in two
    // cells has two selves and the rule cannot say which it meant.
    private (IReadOnlyList<Violation>, SelectionAdmission?, FamilySubject?) ResolveFamilySubject(
        Selection subject, EachNoun noun)
    {
        IReadOnlyList<Selection> cells = _selections.Cells(subject);
        RequireDistinctLayerCells(noun.Layers);

        (List<SelectionAdmission> collected, List<Violation> violations) = CollectSubjectParts(cells);
        if (violations.Count > 0) return (violations, null, null);

        SelectionAdmission whole = SelectionAdmission.Folded(_selections, subject, collected);
        RequireDisjointLayerCells(noun.Layers, collected, whole.Members);

        List<SelectionAdmission> perCell = collected.Select(cell => cell.Restricted(whole.Members)).ToList();
        return (Array.Empty<Violation>(), whole, new FamilySubject(cells, perCell, noun.Layers));
    }

    // The degenerate overlap, caught on the names alone before any type is resolved: a layer listed twice
    // is one cell wearing two, which no membership answer could untangle. A project family cannot reach
    // this — its cells come from a resolved project list, which holds each project once.
    private static void RequireDistinctLayerCells(IReadOnlyList<Layer>? cells)
    {
        if (cells is null) return;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Layer cell in cells)
            if (!seen.Add(cell.Name))
                throw new RuleEvaluationException($"The {cell.Name} layer is listed twice in the family.");
    }

    // Partition discipline over the cells as declared (GRAMMAR §5.1): a subject type in two layer cells has
    // two selves, so the rule fails closed naming the type and both cells. A project family needs no check
    // — a multiply-declared type sits in every declarer's cell and is judged per instance (§4.1). The
    // report is ordinal by type then by cell pair so one overlapping type always names the same one.
    private static void RequireDisjointLayerCells(
        IReadOnlyList<Layer>? cells, IReadOnlyList<SelectionAdmission> declared, HashSet<TypeNode> members)
    {
        if (cells is null || cells.Count < 2) return;

        var owner = new Dictionary<TypeNode, int>();
        List<(string Type, string First, string Second)>? overlaps = null;
        for (var cell = 0; cell < declared.Count; cell++)
            foreach (TypeNode node in declared[cell].Members)
            {
                if (!members.Contains(node)) continue;
                if (!owner.TryGetValue(node, out int first))
                {
                    owner[node] = cell;
                    continue;
                }

                overlaps ??= [];
                overlaps.Add((node.FullName, cells[first].Name, cells[cell].Name));
            }

        if (overlaps is null) return;

        (string type, string firstCell, string secondCell) = overlaps
            .OrderBy(overlap => overlap.Type, StringComparer.Ordinal)
            .ThenBy(overlap => overlap.First, StringComparer.Ordinal)
            .ThenBy(overlap => overlap.Second, StringComparer.Ordinal)
            .First();

        throw new RuleEvaluationException(
            $"Type `{type}` sits in both the {firstCell} and {secondCell} cells of the family; "
            + "a family's cells must not overlap.");
    }

    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) Shape(HashSet<TypeNode> subjects, Func<TypeNode, bool> holds)
    {
        return Shape(subjects, subject => holds(subject) ? null : subject.DeclarationSites);
    }

    // The one minter for every type-subject shape violation: the verdict answers with the sites a red
    // points at, null for a pass. Most shape verbs point at the failing subject's own declaration (the
    // predicate form above); a verb whose evidence lives elsewhere answers with those sites instead.
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) Shape(
        HashSet<TypeNode> subjects, Func<TypeNode, IReadOnlyList<SourceLocation>?> verdict)
    {
        var violations = new List<Violation>();
        foreach (TypeNode subject in subjects)
            if (verdict(subject) is { } sites)
                violations.Add(Violation.Shape(subject, sites));

        return (violations, NoWarnings);
    }

    // The correspondence verb (GRAMMAR §5.3): per subject the template derives a name, and exactly one type
    // in the among selection must carry it. Two arms, each with its own evidence — a subject with NO
    // counterpart is sited at its own declaration, an AMBIGUOUS one at the counterparts that collide — and
    // one identity: the key is the subject's symbol ID either way, because counterparts are evidence and
    // never identity, so a grandfathered subject stays grandfathered when the arm flips. The among selection
    // resolves in SUBJECT position, like a membership: it names where a counterpart may live rather than
    // the far end of an edge. An among selection matching nothing reds every subject and warns about none
    // of it — the shape family raises no inert-target warning.
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) Counterparts(
        HashSet<TypeNode> subjects, MustHaveExactlyOneCounterpartConstraint constraint)
    {
        SelectionAdmission among =
            SelectionAdmission.Operands(_selections, constraint.Among, SelectionPosition.Subject);
        ILookup<string, TypeNode> byName = among.Members.ToLookup(node => node.Name, StringComparer.Ordinal);
        string template = constraint.Template;

        return Shape(subjects, subject =>
        {
            // Materialized rather than held as the lookup's IEnumerable, because the ambiguous arm walks
            // the same counterparts a second time to site them.
            List<TypeNode> counterparts = byName[Derive(template, subject)].ToList();
            if (counterparts.Count == 1) return null;

            return counterparts.Count == 0 ? subject.DeclarationSites : CounterpartSites(counterparts);
        });
    }

    // The name one subject's template derives: every {Name} occurrence replaced by the subject's simple
    // name. netstandard2.0's two-string Replace is ordinal by definition — there is no StringComparison
    // overload to spell it with — which is the same match the spec-build placeholder check runs, so a
    // '{name}' typo fails at build rather than deriving a constant name here.
    private static string Derive(string template, TypeNode subject)
    {
        return template.Replace("{Name}", subject.Name);
    }

    // The ambiguous arm's evidence: every colliding counterpart's declaration, ordered the way a report
    // prints sites. Deliberately the counterparts rather than the subject — the edit that resolves an
    // ambiguity happens at one of them, and the subject is already named by the violation itself.
    private static IReadOnlyList<SourceLocation> CounterpartSites(IEnumerable<TypeNode> counterparts)
    {
        return counterparts.SelectMany(counterpart => counterpart.DeclarationSites)
            .OrderBy(site => site.FilePath, StringComparer.Ordinal)
            .ThenBy(site => site.Line)
            .ToList();
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
        List<Func<TSubject, bool>> matchers = anchors.Select(matcher).ToList();
        return subject =>
        {
            for (var i = 0; i < matchers.Count; i++)
                if (matchers[i](subject))
                    return false;

            return true;
        };
    }

    // The member modal verbs (GRAMMAR §5.7): resolve the member subject, then test each surviving member
    // against the verb's shape/naming/accessibility/flag predicate — a failing member is a MemberShape
    // violation at its own declaration sites, identity keyed on its DocId (§4.6). An empty member subject
    // fails with the member-flavored message (the analog of the empty type subject). Resolution can throw
    // RuleEvaluationException (a closed-generic .Returning anchor); ArchChecker turns that into a RuleError.
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>, SubjectCoverage) EvaluateMember(
        MemberConstraint constraint, HashSet<TypeNode> sourceTypes)
    {
        // MemberConstraint.Subject IS MemberSubject.Source, so the subject the gate already resolved IS
        // this member selection's source types — read from there rather than evaluated a second time. The
        // members themselves take the set alone: a member verb is shape-only, with no edge to attribute.
        IReadOnlyList<MemberNode> members = MemberSelectionEvaluator.Resolve(constraint.MemberSubject, sourceTypes);
        if (members.Count == 0)
            return (
            [
                Violation.EmptySubject(
                    EmptyMemberSubjectMessage, AuthoringHints.ForMemberSubject(_incompleteModel))
            ], NoWarnings, default);

        (IReadOnlyList<Violation> violations, IReadOnlyList<CheckWarning> warnings) = DispatchMember(constraint, members);
        return (violations, warnings, MemberCoverageOf(members));
    }

    // The member-verb dispatch, lifted out of EvaluateMember unchanged for the same reason Dispatch was.
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) DispatchMember(
        MemberConstraint constraint, IReadOnlyList<MemberNode> members)
    {
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
            case MemberMustBeGetOnlyConstraint:
                // STRICT (GRAMMAR §5.7): lawful iff the property declares NO setter accessor at all. An
                // init-only setter IS a setter, so `{ get; init; }` reds — get-only is a claim about the
                // declaration, not about when the write is allowed to happen.
                return MemberShape(members, m => !m.HasSetter);
            case MemberMustBeReadonlyConstraint:
                // A const field SATISFIES the verb (GRAMMAR §5.7): const is readonly's superset — readonly,
                // static and compile-time at once — so redding one would demand something weaker than what
                // is already there.
                return MemberShape(members, m => m.IsReadOnly || m.IsConst);
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
                // open-generic anchor on any construction, GRAMMAR §4.6), and a string anchor passes through
                // verbatim because it already IS that form. Resolving eagerly here is the check-time
                // closed-generic BACKSTOP for the typeof arm: a constructed anchor throws RuleEvaluationException
                // (→ RuleError) before any member is tested. (.Returning's backstop fires a step earlier, inside
                // subject resolution — a divergence observable only for a validation-bypassed empty subject.)
                string parameterAnchor = SelectionEvaluator.DefinitionFullName(
                    c.Anchor, "member parameter matching is definition-level. Anchor on the open definition instead.");
                return MemberShape(members, m => AcceptsParameter(m, parameterAnchor));
            case MemberMustConstraint c:
                return MemberShape(members, m => SelectionEvaluator.InvokePredicate(c.Predicate, m, "Must"));
            default:
                // Fail closed: as with the type-subject switch, an unhandled member verb is a missing
                // arm, not a pass — throw so it surfaces (contained per-rule by ArchChecker), never green.
                throw new InvalidOperationException($"Unhandled member constraint '{constraint.GetType().Name}'.");
        }
    }

    // The parameter scan behind MustAcceptParameter: an index walk rather than a LINQ Any, because it runs
    // once per member of the subject.
    private static bool AcceptsParameter(IMemberInfo member, string parameterAnchor)
    {
        IReadOnlyList<IParameterInfo> parameters = member.Parameters;
        for (var i = 0; i < parameters.Count; i++)
            if (parameters[i].TypeFullName == parameterAnchor)
                return true;

        return false;
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

    // The packaging verbs (GRAMMAR §4.10): resolve the project subject, then test each surviving project
    // against the verb's artifact predicate. An empty project subject fails with the project-flavored
    // message (the analog of the empty type and member subjects). Coverage is the zero pair — it counts
    // TYPES, and a project rule materializes none, so reporting anything else would be a claim about a set
    // this rule never had.
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>, SubjectCoverage) EvaluateProject(
        ProjectConstraint constraint)
    {
        IReadOnlyList<ProjectNode> subjects = ProjectSelectionEvaluator.Resolve(constraint.ProjectSubject, _projects);
        if (subjects.Count == 0)
            return (
            [
                Violation.EmptySubject(
                    EmptyProjectSubjectMessage, AuthoringHints.ForProjectSubject(_incompleteModel))
            ], NoWarnings, default);

        IReadOnlyList<Violation> violations = DispatchProject(constraint, subjects);
        return (violations, NoWarnings, default);
    }

    // The project-verb dispatch, lifted out of EvaluateProject for the same reason Dispatch was. Every arm
    // holds one honesty rule: a fact nothing evaluated is null, and unknown PASSES — a rule that redded on
    // an unevaluated project would be reporting the load's own gaps as architecture violations. No arm has
    // anything advisory to say, so the packaging stratum answers in violations alone and EvaluateProject
    // supplies the empty warning list once.
    private static IReadOnlyList<Violation> DispatchProject(
        ProjectConstraint constraint, IReadOnlyList<ProjectNode> subjects)
    {
        switch (constraint)
        {
            case MustOnlyTargetConstraint c:
                // All over an empty framework list is vacuously true, which is exactly the absent-fact pass:
                // a project nothing evaluated declares nothing to judge.
                var permitted = new HashSet<string>(c.Frameworks, StringComparer.Ordinal);
                return ProjectShape(
                    subjects,
                    project => project.TargetFrameworks.All(permitted.Contains),
                    project => project.TargetFrameworksSite);
            case MustReferenceNoPackagesConstraint:
                return ForbiddenPackages(subjects);
            case MustLockPackagesConstraint:
                // Null passes and false reds: `!= false` is the tri-state, written as the one comparison
                // that says so rather than as a two-arm test that reads like a bug.
                return ProjectShape(
                    subjects, project => project.LocksPackages != false, project => project.LocksPackagesSite);
            case MustNotBePackableConstraint:
                return ProjectShape(
                    subjects, project => project.IsPackable != true, project => project.IsPackableSite);
            case ProjectMustConstraint c:
                // No site: a predicate is a claim about the whole project, and no single declaration in it is
                // the one that failed. An unlocated violation is the honest form of that.
                return ProjectShape(
                    subjects,
                    project => SelectionEvaluator.InvokePredicate(c.Predicate, project, "Must"),
                    _ => null);
            default:
                // Fail closed: as with the type- and member-subject switches, an unhandled project verb is a
                // missing arm, not a pass — throw so it surfaces (contained per-rule by ArchChecker).
                throw new InvalidOperationException($"Unhandled project constraint '{constraint.GetType().Name}'.");
        }
    }

    // The one walk behind the packaging shape verbs: one violation per failing project, sited at whatever
    // declared the fact the verb read — which is regularly a props file above the project, and is null where
    // nothing evaluated it.
    private static IReadOnlyList<Violation> ProjectShape(
        IReadOnlyList<ProjectNode> subjects, Func<ProjectNode, bool> holds, Func<ProjectNode, SourceLocation?> siteOf)
    {
        var violations = new List<Violation>();
        foreach (ProjectNode subject in subjects)
            if (!holds(subject))
                violations.Add(Violation.ProjectShape(subject, AtSite(siteOf(subject))));

        return violations;
    }

    // MustReferenceNoPackages (GRAMMAR §4.10): one violation per DECLARED package, sited at that reference's
    // own declaration, so a project taking eight packages reports eight lines to delete rather than one line
    // saying eight. A project declaring none passes — and so does a project nothing evaluated, because an
    // empty package list is those two states wearing one face and the honest reading of both is silence.
    private static IReadOnlyList<Violation> ForbiddenPackages(IReadOnlyList<ProjectNode> subjects)
    {
        var violations = new List<Violation>();
        foreach (ProjectNode subject in subjects)
        foreach (PackageReference package in subject.PackageReferences)
            violations.Add(Violation.ProjectPackage(subject, package));

        return violations;
    }

    // A nullable fact site as a violation's evidence list: the one site it has, or nothing to point at.
    private static IReadOnlyList<SourceLocation> AtSite(SourceLocation? site)
    {
        return site is null ? Array.Empty<SourceLocation>() : [site];
    }

    // A verb's forbidden set or allow-list: every operand resolved in target position and folded into one
    // admission, which carries both the membership the non-edge tests read and the per-node attribution
    // an edge test needs where several projects declare one type (GRAMMAR §4.1).
    private SelectionAdmission ResolveOperands(IReadOnlyList<Selection> operands)
    {
        return SelectionAdmission.Operands(_selections, operands, SelectionPosition.Target);
    }

    // One edge kind's index, built on first use so a spec that never uses (say) a catch verb never pays
    // for a catch index. Keyed on the endpoint the SUBJECT set is tested against — Source for the
    // outbound verbs, Target for the two referenced-by verbs — with the default (reference) comparer,
    // the same identity HashSet<TypeNode>.Contains used before them. A key is a node, not a name, and the
    // distinction is real rather than pedantic: a name a project declares and a referenced assembly also
    // supplies denotes two nodes (GRAMMAR §4.1), and a reference into one of them must not index under the
    // other. Reference identity is what keeps them apart — do not give TypeNode value equality on FullName.
    private sealed class EdgeIndex<TEdge>
    {
        private readonly IReadOnlyList<TEdge> _edges;
        private readonly Func<TEdge, TypeNode> _key;
        private ILookup<TypeNode, TEdge>? _lookup;

        internal EdgeIndex(IReadOnlyList<TEdge> edges, Func<TEdge, TypeNode> key)
        {
            _edges = edges;
            _key = key;
        }

        internal ILookup<TypeNode, TEdge> Lookup => _lookup ??= _edges.ToLookup(_key);
    }

    // A family subject resolved once per rule (GRAMMAR §5.1): the cells themselves, and the subject each
    // cell's law ranges over — that cell intersected with the family's own membership, so an Except on the
    // family narrows what every cell governs. Held rather than recomputed because four verbs read it and
    // a project family resolves its cells against the project list to find them. The cells as DECLARED are
    // not held here: only the verbs that read the cells as declared need them, and they resolve them themselves.
    private sealed class FamilySubject(
        IReadOnlyList<Selection> cells,
        IReadOnlyList<SelectionAdmission> subjects,
        IReadOnlyList<Layer>? layerCells)
    {
        /// <summary>The cells, in declaration order for a layer family and project order for a project one.</summary>
        internal IReadOnlyList<Selection> Cells { get; } = cells;

        /// <summary>Each cell intersected with the family's membership, positionally aligned with <see cref="Cells" />.</summary>
        internal IReadOnlyList<SelectionAdmission> Subjects { get; } = subjects;

        /// <summary>The declared layer cells, or null for a project family — the partition's own kind.</summary>
        internal IReadOnlyList<Layer>? LayerCells { get; } = layerCells;

        /// <summary>
        ///     The name of the cell at <paramref name="index" /> — the layer's, or the project's for a
        ///     project family — which is what a violation minted under that cell's law is labelled with.
        /// </summary>
        /// <remarks>
        ///     Read off the cell's own noun rather than held beside it: a project cell is minted as
        ///     <c>arch.Project(name)</c> would be, so the name it resolved to is already there. A cell can
        ///     be nothing else, and a noun with no name is a new cell kind whose label nobody has decided —
        ///     so it throws rather than grouping the burndown under a blank.
        /// </remarks>
        internal string CellName(int index)
        {
            return SelectionWalk.NounOf(Cells[index]) switch
            {
                LayerNoun layer => layer.Name,
                ProjectNoun project => project.Name,
                { } noun => throw new InvalidOperationException($"Unhandled family cell noun '{noun.GetType().Name}'."),
                null => throw new InvalidOperationException("A family cell is never a union.")
            };
        }
    }
}
