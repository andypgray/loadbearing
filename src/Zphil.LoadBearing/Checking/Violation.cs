using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Checking;

/// <summary>
///     One concrete way a rule was broken: a reference between two types, a use of a banned member, a
///     type, member or project of the wrong shape, a subject that matched nothing, or an error that
///     stopped the rule being evaluated. <see cref="Kind" /> says which, and therefore which of the
///     properties below carry a value and which are null; <see cref="Sites" /> carries the source
///     positions that evidence it. A rule's failing violations are in
///     <see cref="RuleResult.Violations" /> and its tolerated ones in
///     <see cref="RuleResult.Grandfathered" />.
/// </summary>
// The constructor takes only what every violation has — its kind and its evidence — and each factory
// below names the slots its own kind populates, through an object initializer. That is why the slots
// carry a private setter rather than being get-only: init is unavailable on this target framework (no
// IsExternalInit), and every factory is a member of this class, so the setters reach exactly as far
// as they must and a violation is read-only to every consumer. A new slot therefore touches no
// existing factory, and no factory threads a run of nulls past slots of the same type where a
// transposition would compile.
public sealed class Violation
{
    private Violation(ViolationKind kind, IReadOnlyList<SourceLocation> sites)
    {
        Kind = kind;
        Sites = sites;
    }

    /// <summary>Gets what kind of violation this is, and so which of the properties below carry a value.</summary>
    public ViolationKind Kind { get; }

    /// <summary>
    ///     Gets the type at the near end of the offending edge: the one that references, constructs,
    ///     injects, catches, throws or exposes, depending on <see cref="Kind" />. This is where the edit
    ///     goes, for the inbound verbs as much as the outbound ones. Null for every kind that is not an
    ///     edge.
    /// </summary>
    public TypeNode? Source { get; private set; }

    /// <summary>
    ///     Gets the type at the far end of the offending edge: the referenced type, the constructed type,
    ///     the injected parameter type, the caught or the thrown exception type, or the exposed type,
    ///     depending on <see cref="Kind" />. Null for every kind that is not an edge.
    /// </summary>
    public TypeNode? Target { get; private set; }

    /// <summary>
    ///     Gets the type that failed a verb about the type itself (its shape, its name, what it inherits
    ///     or is attributed with, where it lives, or a <c>Must</c> predicate); null for every other kind.
    /// </summary>
    public TypeNode? Subject { get; private set; }

    /// <summary>Gets the banned member the source used; null for every kind but a member use.</summary>
    public MemberReference? Member { get; private set; }

    /// <summary>Gets the declared member that failed a member rule; null for every other kind.</summary>
    public MemberNode? SubjectMember { get; private set; }

    /// <summary>Gets the project that failed a packaging or targeting rule; null for every other kind.</summary>
    public ProjectNode? SubjectProject { get; private set; }

    /// <summary>
    ///     Gets the declared package reference that counts against the rule. Populated only by the
    ///     per-package violations of <c>MustReferenceNoPackages</c>, one for each package the project
    ///     declares, and null on every other project violation and every other kind: its presence is what
    ///     parts "this project is wrong" from "this project declares this package".
    /// </summary>
    public PackageReference? Package { get; private set; }

    /// <summary>
    ///     Gets the source positions that evidence the violation: the sites of the offending edge, or the
    ///     declarations of the offending type, member or project fact. For a project fact that is the
    ///     declaration that won the build's evaluation, regularly a props file above the project file, and
    ///     the project file itself where nothing declared the property. Empty when the subject matched
    ///     nothing and when the rule errored, neither of which has anywhere to point.
    /// </summary>
    public IReadOnlyList<SourceLocation> Sites { get; }

    /// <summary>
    ///     Gets the explanatory text of a violation that has no code to point at: why the subject matched
    ///     nothing, or what stopped the rule being evaluated. On a reference violation from
    ///     <c>MustNotHaveCircularReferences</c> it instead names the circle the pair lies on ("circular
    ///     references among the A and B layers"), which <c>--json</c> output alone prints. Null otherwise.
    /// </summary>
    public string? Detail { get; private set; }

    /// <summary>
    ///     Gets what to change so the rule's subject matches something, on a violation that is about the
    ///     rule rather than about the code: the selection's own semantics, and what to check first. Present
    ///     on every violation raised because the subject selected nothing, and null on every other kind,
    ///     whose fix is a change to the code the violation names. The check report prints it on a line
    ///     under the violation, and <c>--json</c> output carries it as <c>hint</c>.
    /// </summary>
    public string? Hint { get; private set; }

    /// <summary>
    ///     This violation's deterministic within-rule report order key: (Source|Subject FullName, Target
    ///     FullName, Member SymbolId, Target|Subject ProjectName), compared ordinal by the checker. A
    ///     MemberUse mirrors Reference's (source, target) as (source FullName, member SymbolId); a
    ///     MemberShape mirrors Shape's subject as (declaring-type FullName, member SymbolId); a
    ///     ProjectShape mirrors it as (project Name, package Name), the package name standing where a
    ///     Target would, so one project's per-package violations sort by the package they name.
    /// </summary>
    /// <remarks>
    ///     The fourth slot exists because the first three no longer separate every pair of violations: a full
    ///     name a project declares and a referenced assembly also supplies denotes two nodes (GRAMMAR §4.1),
    ///     and one source type can reach both where it is itself compiled into two projects. Without it the
    ///     sort ties, and a stable sort then falls through to the order the subject set was walked in — a
    ///     reference-keyed hash set, which is to say no order at all, differing run to run. Project name is
    ///     the discriminator because it is exactly what parts the two: the external half carries the
    ///     supplying assembly's name.
    /// </remarks>
    internal (string Primary, string Secondary, string Tertiary, string Quaternary) OrderKey
    {
        get
        {
            // A MemberShape's declaring-type FullName and a ProjectShape's project Name are the only
            // primary keys that are not a Source or a Subject; every other kind leaves both null and never
            // reaches them.
            string primary = (Source ?? Subject)?.FullName
                             ?? SubjectProject?.Name
                             ?? (SubjectMember is { } member ? member.DeclaringTypeFullName : string.Empty);
            string secondary = Target?.FullName ?? Package?.Name ?? string.Empty;
            string tertiary = Member?.SymbolId ?? SubjectMember?.SymbolId ?? string.Empty;
            string quaternary = (Target ?? Subject)?.ProjectName ?? string.Empty;
            return (primary, secondary, tertiary, quaternary);
        }
    }

    /// <summary>
    ///     Returns the entry that identifies this violation in a baseline file — what decides whether a
    ///     captured baseline grandfathers it — or null for a violation no baseline can ever hold.
    /// </summary>
    /// <remarks>
    ///     An edge key for the edge kinds, pairing <see cref="Source" />'s symbol ID with
    ///     <see cref="Target" />'s, or with <see cref="Member" />'s for a banned member use. A subject key
    ///     otherwise: <see cref="Subject" />'s symbol ID for a type, <see cref="SubjectMember" />'s for a
    ///     member, and <see cref="SubjectProject" />'s for a project — so all of one project's per-package
    ///     violations share the single entry that blesses the project, and the entry survives the package
    ///     list changing. Every overload, parameter, catch clause, throw and signature position of one pair
    ///     rides under one identity, its sites being evidence rather than identity; an edge entry also
    ///     records how many sites it covered when it was captured, and a pair that has since grown past that
    ///     count fails the rule instead of being grandfathered. Null for a subject that matched nothing and
    ///     for a rule that errored: those have no stable identity.
    /// </remarks>
    public BaselineEntry? BaselineIdentity()
    {
        return Kind switch
        {
            ViolationKind.Reference or ViolationKind.Construction or ViolationKind.Injection
                or ViolationKind.Catch or ViolationKind.Throw or ViolationKind.Expose =>
                BaselineEntry.ForEdge(Source!.SymbolId, Target!.SymbolId),
            ViolationKind.MemberUse => BaselineEntry.ForEdge(Source!.SymbolId, Member!.SymbolId),
            ViolationKind.Shape => BaselineEntry.ForSubject(Subject!.SymbolId),
            ViolationKind.MemberShape => BaselineEntry.ForSubject(SubjectMember!.SymbolId),
            // Both project factories key the same way, so the per-package violations of one project share
            // one identity: the law is about the project, and a baseline entry blessing it must not have to
            // be rewritten every time the package list moves.
            ViolationKind.ProjectShape => BaselineEntry.ForSubject(SubjectProject!.SymbolId),
            _ => null
        };
    }

    internal static Violation Reference(TypeNode source, TypeNode target, IReadOnlyList<SourceLocation> sites)
    {
        return Edge(ViolationKind.Reference, source, target, sites);
    }

    /// <summary>
    ///     A reference violation carrying the circle its pair lies on — what
    ///     <c>MustNotHaveCircularReferences</c> mints (GRAMMAR §5.3). Identical to
    ///     <see cref="Reference(TypeNode, TypeNode, IReadOnlyList{SourceLocation})" /> in kind, identity and
    ///     order key, so the baseline, the human report and SARIF are untouched; <see cref="Detail" /> is
    ///     the JSON channel's addition alone.
    /// </summary>
    internal static Violation Reference(
        TypeNode source, TypeNode target, IReadOnlyList<SourceLocation> sites, string detail)
    {
        return new Violation(ViolationKind.Reference, sites) { Source = source, Target = target, Detail = detail };
    }

    internal static Violation Construction(TypeNode source, TypeNode target, IReadOnlyList<SourceLocation> sites)
    {
        return Edge(ViolationKind.Construction, source, target, sites);
    }

    internal static Violation Injection(TypeNode source, TypeNode target, IReadOnlyList<SourceLocation> sites)
    {
        return Edge(ViolationKind.Injection, source, target, sites);
    }

    internal static Violation Catch(TypeNode source, TypeNode target, IReadOnlyList<SourceLocation> sites)
    {
        return Edge(ViolationKind.Catch, source, target, sites);
    }

    internal static Violation Throw(TypeNode source, TypeNode target, IReadOnlyList<SourceLocation> sites)
    {
        return Edge(ViolationKind.Throw, source, target, sites);
    }

    internal static Violation Expose(TypeNode source, TypeNode target, IReadOnlyList<SourceLocation> sites)
    {
        return Edge(ViolationKind.Expose, source, target, sites);
    }

    internal static Violation MemberUse(TypeNode source, MemberReference member, IReadOnlyList<SourceLocation> sites)
    {
        return new Violation(ViolationKind.MemberUse, sites) { Source = source, Member = member };
    }

    internal static Violation Shape(TypeNode subject, IReadOnlyList<SourceLocation> sites)
    {
        return new Violation(ViolationKind.Shape, sites) { Subject = subject };
    }

    internal static Violation MemberShape(MemberNode subjectMember, IReadOnlyList<SourceLocation> sites)
    {
        return new Violation(ViolationKind.MemberShape, sites) { SubjectMember = subjectMember };
    }

    /// <summary>
    ///     A subject project failing a packaging or escape verb (GRAMMAR §4.10), evidenced by the offending
    ///     fact's declaration sites — which may be empty, because a fact nothing declared has nowhere to
    ///     point and an unlocated finding is more honest than a made-up one.
    /// </summary>
    internal static Violation ProjectShape(ProjectNode subject, IReadOnlyList<SourceLocation> sites)
    {
        return new Violation(ViolationKind.ProjectShape, sites) { SubjectProject = subject };
    }

    /// <summary>
    ///     One declared package reference counting against <c>MustReferenceNoPackages</c>, sited at the
    ///     reference's own declaration — which may be a props file above the project. Shares
    ///     <see cref="ProjectShape" />'s kind and its project-keyed identity; <see cref="Package" /> is what
    ///     tells the two apart.
    /// </summary>
    internal static Violation ProjectPackage(ProjectNode subject, PackageReference package)
    {
        return new Violation(ViolationKind.ProjectShape, [package.Site]) { SubjectProject = subject, Package = package };
    }

    internal static Violation EmptySubject(string detail, string hint)
    {
        return new Violation(ViolationKind.EmptySubject, Array.Empty<SourceLocation>())
        {
            Detail = detail, Hint = hint
        };
    }

    internal static Violation RuleError(string detail)
    {
        return new Violation(ViolationKind.RuleError, Array.Empty<SourceLocation>()) { Detail = detail };
    }

    // The one mint the six edge factories share: an edge violation is a (source, target) pair with its
    // sites, and every other slot empty.
    private static Violation Edge(
        ViolationKind kind, TypeNode source, TypeNode target, IReadOnlyList<SourceLocation> sites)
    {
        return new Violation(kind, sites) { Source = source, Target = target };
    }
}
