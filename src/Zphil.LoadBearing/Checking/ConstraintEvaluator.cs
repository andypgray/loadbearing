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
    private readonly EdgeIndex<InjectionEdge> _injectionEdgesBySource;
    private readonly EdgeIndex<MemberEdge> _memberEdgesBySource;
    private readonly SelectionEvaluator _selections;
    private readonly EdgeIndex<ThrowEdge> _throwEdgesBySource;

    internal ConstraintEvaluator(CodebaseModel model, SelectionEvaluator selections)
    {
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
        // Loud per-operand emptiness for a union subject (GRAMMAR §9), ahead of the member dispatch because
        // MemberConstraint.Subject IS the underlying type selection — so one gate covers the type- and
        // member-subject paths alike. The gate hands back the admission it folded the operands into, so
        // the union subject is neither evaluated a second time nor attributed a second time.
        (IReadOnlyList<Violation> emptyOperands, SelectionAdmission? resolved) = ResolveSubject(constraint.Subject);
        if (resolved is not { } admission) return (emptyOperands, NoWarnings, default);

        // A member-subject constraint (GRAMMAR §4.6) ranges over declared members, so it dispatches before
        // the type-subject gate: its own empty check speaks in member terms (a type subject that matches
        // types none of whose members survive the kind filter is the ordinary way to fail empty).
        if (constraint is MemberConstraint memberConstraint) return EvaluateMember(memberConstraint, admission.Members);

        HashSet<TypeNode> subjects = admission.Members;
        if (subjects.Count == 0) return ([Violation.EmptySubject(EmptySubjectMessage)], NoWarnings, default);

        (IReadOnlyList<Violation> violations, IReadOnlyList<CheckWarning> warnings) = Dispatch(constraint, subjects, admission);
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
    // Only the two INVERSE verbs read the admission beside the set: there the subject sits at the edge's
    // target end, where which project a reference is attributed to decides whether it counts (§4.1).
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) Dispatch(
        Constraint constraint, HashSet<TypeNode> subjects, SelectionAdmission admission)
    {
        switch (constraint)
        {
            case MustNotReferenceConstraint c:
                return ForbiddenReference(admission, c.Targets, inbound: false);
            case MustNotBeReferencedByConstraint c:
                return ForbiddenReference(admission, c.Sources, inbound: true);
            case MustOnlyReferenceConstraint c:
                return OnlyReference(subjects, c.Targets);
            case MustOnlyBeReferencedByConstraint c:
                return OnlyBeReferencedBy(admission, c.Sources);
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
    ///     resolve the operands, keep each candidate edge whose non-subject endpoint is a forbidden
    ///     operand, mint one violation per survivor from the sites that verb treats as evidence, and — for
    ///     the arms that warn — raise the inert-target warning. Inert only when the forbidden operand set
    ///     is empty AND at least one operand is a pattern selection; a bare <c>typeof</c> target absent
    ///     from the codebase is the win condition, not a warning. <c>requireSites</c> is the refinement
    ///     the two catch subsets carry: an edge none of whose sites the ban forbids is green, and no
    ///     printed <c>file:line</c> is ever a site the ban permits.
    /// </summary>
    /// <remarks>
    ///     <paramref name="attributionSourceOf" /> names the endpoint whose compilation made the edge —
    ///     the source, for every verb whose operand sits at the target end. It is what tells a
    ///     project-headed operand a reference into a type that project compiles itself from a reference
    ///     into another project's declaration of the same name (GRAMMAR §4.1). Null hands the test back to
    ///     plain membership, which is what the inbound reference verb wants: there the operand IS the
    ///     edge's source, and every project declaring it genuinely makes the reference.
    /// </remarks>
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) ForbiddenEdge<TEdge>(
        IEnumerable<TEdge> candidates,
        IReadOnlyList<Selection> operands,
        Func<TEdge, TypeNode> operandOf,
        Func<TEdge, IReadOnlyList<SourceLocation>> sitesOf,
        Func<TEdge, IReadOnlyList<SourceLocation>, Violation> toViolation,
        bool requireSites,
        bool warnInert,
        Func<TEdge, TypeNode>? attributionSourceOf)
    {
        SelectionAdmission operandSet = ResolveOperands(operands);
        var violations = new List<Violation>();

        foreach (TEdge edge in candidates)
        {
            TypeNode operand = operandOf(edge);
            bool forbidden = attributionSourceOf is null
                ? operandSet.Contains(operand)
                : operandSet.Admits(operand, attributionSourceOf(edge));

            if (!forbidden) continue;

            IReadOnlyList<SourceLocation>? sites = sitesOf(edge);
            if (requireSites && sites.Count == 0) continue;

            violations.Add(toViolation(edge, sites));
        }

        IReadOnlyList<CheckWarning> warnings = warnInert
                                               && violations.Count == 0
                                               && operandSet.Count == 0
                                               && operands.Any(SelectionEvaluator.IsPatternSelection)
            ? [new CheckWarning(CheckWarningKind.InertTarget, InertTargetMessage)]
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
    ///     The two directions carry the §4.1 attribution rule at opposite ends. Outbound it rides on the
    ///     operand, which is the edge's target. Inbound the SUBJECT is the target, so it rides on the
    ///     candidates instead: a project-headed subject counts an inbound reference only where the
    ///     referencing compilation bound the copy that subject's project declares. Filtering candidates
    ///     rather than violations is safe because walk order is unobservable (see <see cref="Keyed" />).
    /// </remarks>
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) ForbiddenReference(
        SelectionAdmission subjects, IReadOnlyList<Selection> operands, bool inbound)
    {
        ILookup<TypeNode, ReferenceEdge> index = inbound ? _edgesByTarget.Lookup : _edgesBySource.Lookup;
        IEnumerable<ReferenceEdge> candidates = Keyed(subjects.Members, index);
        if (inbound) candidates = candidates.Where(edge => subjects.Admits(edge.Target, edge.Source));

        Func<ReferenceEdge, TypeNode> operandOf = inbound ? e => e.Source : e => e.Target;
        Func<ReferenceEdge, TypeNode>? attributionSourceOf = inbound ? null : e => e.Source;

        return ForbiddenEdge(
            candidates, operands, operandOf, e => e.Sites,
            (e, sites) => Violation.Reference(e.Source, e.Target, sites),
            requireSites: false, warnInert: true, attributionSourceOf: attributionSourceOf);
    }

    // The member-access verb (GRAMMAR §4.5): a member edge is a hit when its source is a subject AND
    // its used member matches a banned (declaring type, name) pair — ordinal, one ban covering every
    // overload. Per-overload edges yield per-overload MemberUse violations (the §4.3 identity substrate).
    // The banned set is resolved eagerly so a closed-generic member anchor is refused (RuleError) before
    // any edge is tested — mirroring the type-noun refusal in SelectionEvaluator.DefinitionFullName.
    // Keyed on names rather than on nodes, the ban is attribution-insensitive by construction, which is
    // the same answer a typeof operand gets from the §4.1 attribution rule: a member of a type several
    // projects compile is banned in every one of them, its own compiled-in copy included.
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
        HashSet<TypeNode> subjects, IReadOnlyList<Selection> operands)
    {
        return ForbiddenEdge(
            Keyed(subjects, _constructorEdgesBySource.Lookup), operands, e => e.Constructed, e => e.Sites,
            (e, sites) => Violation.Construction(e.Source, e.Constructed, sites),
            requireSites: false, warnInert: true, attributionSourceOf: e => e.Source);
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
        HashSet<TypeNode> subjects, IReadOnlyList<Selection> operands)
    {
        return ForbiddenEdge(
            Keyed(subjects, _injectionEdgesBySource.Lookup), operands, e => e.Injected, e => e.Sites,
            (e, sites) => Violation.Injection(e.Source, e.Injected, sites),
            requireSites: false, warnInert: false, attributionSourceOf: e => e.Source);
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
        HashSet<TypeNode> subjects, IReadOnlyList<Selection> operands)
    {
        return ForbiddenEdge(
            Keyed(subjects, _catchEdgesBySource.Lookup), operands, e => e.Caught, e => e.Sites,
            (e, sites) => Violation.Catch(e.Source, e.Caught, sites),
            requireSites: false, warnInert: true, attributionSourceOf: e => e.Source);
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
        HashSet<TypeNode> subjects, IReadOnlyList<Selection> operands)
    {
        return ForbiddenEdge(
            Keyed(subjects, _catchEdgesBySource.Lookup), operands, e => e.Caught, e => e.UnfilteredSites,
            (e, sites) => Violation.Catch(e.Source, e.Caught, sites),
            requireSites: true, warnInert: true, attributionSourceOf: e => e.Source);
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
        HashSet<TypeNode> subjects, IReadOnlyList<Selection> operands)
    {
        return ForbiddenEdge(
            Keyed(subjects, _catchEdgesBySource.Lookup), operands, e => e.Caught, e => e.SwallowingSites,
            (e, sites) => Violation.Catch(e.Source, e.Caught, sites),
            requireSites: true, warnInert: true, attributionSourceOf: e => e.Source);
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
        HashSet<TypeNode> subjects, IReadOnlyList<Selection> operands)
    {
        return ForbiddenEdge(
            Keyed(subjects, _throwEdgesBySource.Lookup), operands, e => e.Thrown, e => e.Sites,
            (e, sites) => Violation.Throw(e.Source, e.Thrown, sites),
            requireSites: false, warnInert: true, attributionSourceOf: e => e.Source);
    }

    /// <summary>
    ///     The exposure verb (GRAMMAR §4.9, §5.3): an exposure edge is a hit when its source is a subject
    ///     AND the exposed type is a forbidden operand — the "you may use it; you may not surface it on
    ///     your public API" ban. Matching is exact definition-level FQN on the operand set (the family's
    ///     shared node membership): <c>MustNotExpose(typeof(DataTable))</c> flags only a <c>DataTable</c>
    ///     signature position, never a narrower <c>DataView</c> one (no hierarchy-aware matching).
    /// </summary>
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) ForbiddenExposure(
        HashSet<TypeNode> subjects, IReadOnlyList<Selection> operands)
    {
        return ForbiddenEdge(
            Keyed(subjects, _exposureEdgesBySource.Lookup), operands, e => e.Exposed, e => e.Sites,
            (e, sites) => Violation.Expose(e.Source, e.Exposed, sites),
            requireSites: false, warnInert: true, attributionSourceOf: e => e.Source);
    }

    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) OnlyReference(
        HashSet<TypeNode> subjects, IReadOnlyList<Selection> allowedTargets)
    {
        SelectionAdmission allowed = ResolveOperands(allowedTargets);
        var violations = new List<Violation>();

        // Strict, no implicit self-allowance; external targets are exempt (the complement universe is
        // solution-declared, GRAMMAR §4.1). MustOnly* never warns — an empty allow-set is loud by itself.
        // Strictness survives §4.1 attribution: a reference into a type the referencing project compiles
        // itself is allowed by an entry naming THAT project, or by an entry that is not a project at all —
        // never by an entry naming some other declarer of the same source file.
        foreach (ReferenceEdge edge in Keyed(subjects, _edgesBySource.Lookup))
            if (!edge.Target.IsExternal && !allowed.Admits(edge.Target, edge.Source))
                violations.Add(Violation.Reference(edge.Source, edge.Target, edge.Sites));

        return (violations, NoWarnings);
    }

    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) OnlyBeReferencedBy(
        SelectionAdmission subjects, IReadOnlyList<Selection> allowedSources)
    {
        SelectionAdmission allowed = ResolveOperands(allowedSources);
        var violations = new List<Violation>();

        // Any inbound reference from outside the allow-set is a violation (the containment verb, §7).
        // Edge sources are always solution-declared, so no external caveat is needed. The subject sits at
        // the edge's target end, so it counts an edge only where the reference is attributed to it (§4.1),
        // while the allow-set test stays plain membership — every declarer of an allowed source makes the
        // reference itself.
        foreach (ReferenceEdge edge in Keyed(subjects.Members, _edgesByTarget.Lookup))
            if (subjects.Admits(edge.Target, edge.Source) && !allowed.Contains(edge.Source))
                violations.Add(Violation.Reference(edge.Source, edge.Target, edge.Sites));

        return (violations, NoWarnings);
    }

    // The throw verb (GRAMMAR §4.8, §5.3): a STRICT allow-list — every throw edge from a subject whose thrown
    // type is not in the allowed set is a violation, EXTERNAL thrown types included. This is OnlyReference
    // MINUS its external-target exemption: MustOnlyThrow constrains external throws too, so an unlisted
    // System.TimeoutException throw is red unless typeof(TimeoutException) is in the allow-set (a Type-sugar
    // operand resolves the external node by FQN, since the target universe includes externals). An allowed
    // type absent from the model resolves empty and harmlessly allows nothing. MustOnly* never warns — an
    // empty allow-set is loud by itself (the point of departure from ForbiddenCatch). The allow-set is a
    // target position like any other, so §4.1 attribution decides membership here too: a project-headed
    // entry allows a throw of a type several projects compile only where the throwing project is the one
    // the edge is attributed to.
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) OnlyThrow(
        HashSet<TypeNode> subjects, IReadOnlyList<Selection> allowedThrows)
    {
        SelectionAdmission allowed = ResolveOperands(allowedThrows);
        var violations = new List<Violation>();

        foreach (ThrowEdge edge in Keyed(subjects, _throwEdgesBySource.Lookup))
            if (!allowed.Admits(edge.Thrown, edge.Source))
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
    private (IReadOnlyList<Violation> Empty, SelectionAdmission? Subject) ResolveSubject(Selection subject)
    {
        if (subject is not UnionSelection union)
        {
            SelectionAdmission whole = SelectionAdmission.Collect(_selections, subject, SelectionPosition.Subject);
            return (Array.Empty<Violation>(), whole);
        }

        var operands = new List<SelectionAdmission>(union.Parts.Count);
        var violations = new List<Violation>();
        foreach (Selection operand in union.Parts)
        {
            SelectionAdmission matched = SelectionAdmission.Collect(_selections, operand, SelectionPosition.Subject);
            operands.Add(matched);
            if (matched.Count == 0)
                violations.Add(Violation.EmptySubject(EmptyOperandMessage(SentenceRenderer.Reference(operand))));
        }

        if (violations.Count > 0) return (violations, null);

        return (Array.Empty<Violation>(), SelectionAdmission.United(_selections, union, operands));
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
        if (members.Count == 0) return ([Violation.EmptySubject(EmptyMemberSubjectMessage)], NoWarnings, default);

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
                // open-generic anchor on any construction, GRAMMAR §4.6). Resolving eagerly here is the
                // check-time closed-generic BACKSTOP: a constructed anchor throws RuleEvaluationException
                // (→ RuleError) before any member is tested. (.Returning's backstop fires a step earlier, inside
                // subject resolution — a divergence observable only for a validation-bypassed empty subject.)
                string parameterAnchor = SelectionEvaluator.DefinitionFullName(
                    c.ParameterType, "member parameter matching is definition-level. Anchor on the open definition instead.");
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
}
