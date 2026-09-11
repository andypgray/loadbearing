using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Checking;

/// <summary>
///     One concrete way a rule is broken (GRAMMAR §4.3). <see cref="Kind" /> governs which of the
///     nullable slots are populated — <see cref="ViolationKind" /> documents the mapping per kind.
/// </summary>
/// <remarks>
///     The constructor takes only what every violation has — its kind and its evidence — and each factory
///     below names the slots its own kind populates, through an object initializer. That is why the slots
///     carry a private setter rather than being get-only: <c>init</c> is unavailable on this target
///     framework (no <c>IsExternalInit</c>), and every factory is a member of this class, so the setters
///     reach exactly as far as they must and a violation is read-only to every consumer. A new slot
///     therefore touches no existing factory, and no factory threads a run of nulls past slots of the
///     same type where a transposition would compile.
/// </remarks>
public sealed class Violation
{
    private Violation(ViolationKind kind, IReadOnlyList<SourceLocation> sites)
    {
        Kind = kind;
        Sites = sites;
    }

    /// <summary>The violation kind.</summary>
    public ViolationKind Kind { get; }

    /// <summary>
    ///     The referencing type (Reference kind — for the inbound verbs this is where the edit happens), the
    ///     constructing type (Construction kind), the injecting type (Injection kind), the catching type
    ///     (Catch kind), the throwing type (Throw kind), or the exposing type (Expose kind).
    /// </summary>
    public TypeNode? Source { get; private set; }

    /// <summary>
    ///     The referenced type (Reference kind), the constructed type (Construction kind), the injected
    ///     parameter type (Injection kind), the caught exception type (Catch kind), the thrown exception
    ///     type (Throw kind), or the exposed type (Expose kind).
    /// </summary>
    public TypeNode? Target { get; private set; }

    /// <summary>The offending subject type (Shape kind).</summary>
    public TypeNode? Subject { get; private set; }

    /// <summary>The banned member the source used (MemberUse kind); null otherwise.</summary>
    public MemberReference? Member { get; private set; }

    /// <summary>The offending declared member (MemberShape kind); null otherwise.</summary>
    public MemberNode? SubjectMember { get; private set; }

    /// <summary>The offending subject project (ProjectShape kind, GRAMMAR §4.10); null otherwise.</summary>
    public ProjectNode? SubjectProject { get; private set; }

    /// <summary>
    ///     The offending declared package reference — populated only by the per-package
    ///     <c>MustReferenceNoPackages</c> violations, and null on every other ProjectShape and every other
    ///     kind. Its presence is what parts "this project is wrong" from "this project declares this
    ///     package".
    /// </summary>
    public PackageReference? Package { get; private set; }

    /// <summary>The reference or declaration sites carrying the violation; empty for EmptySubject/RuleError.</summary>
    public IReadOnlyList<SourceLocation> Sites { get; }

    /// <summary>
    ///     Free text for EmptySubject/RuleError; on a Reference violation minted by the
    ///     circular-references verb, the circle the pair lies on ("circular references among the A and B
    ///     layers"), which the JSON channel alone reads — never the human report and never SARIF; null
    ///     otherwise.
    /// </summary>
    public string? Detail { get; private set; }

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

    /// <summary>This violation's stable baseline identity (GRAMMAR §4.3).</summary>
    /// <remarks>
    ///     An edge key for the dependency kinds — (<see cref="Source" />, <see cref="Target" />) symbol
    ///     IDs, or (<see cref="Source" /> symbol ID, <see cref="Member" />'s member DocId) for a
    ///     MemberUse — and a subject key for a Shape (<see cref="Subject" />) or a MemberShape
    ///     (<see cref="SubjectMember" />'s member DocId, GRAMMAR §4.6). Each kind's collapse rule — why
    ///     every overload, parameter, catch clause, throw or signature position of one type pair shares
    ///     a single identity — is documented on <see cref="ViolationKind" />.
    ///     <see cref="ViolationKind.EmptySubject" /> and <see cref="ViolationKind.RuleError" /> have no
    ///     stable identity and return null, so they can never be grandfathered.
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

    internal static Violation EmptySubject(string detail)
    {
        return new Violation(ViolationKind.EmptySubject, Array.Empty<SourceLocation>()) { Detail = detail };
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
