using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Checking;

/// <summary>
///     One concrete way a rule is broken (GRAMMAR §4.3). <see cref="Kind" /> governs which of the
///     nullable slots are populated — <see cref="ViolationKind" /> documents the mapping per kind.
/// </summary>
public sealed class Violation
{
    private Violation(
        ViolationKind kind,
        TypeNode? source,
        TypeNode? target,
        TypeNode? subject,
        MemberReference? member,
        MemberNode? subjectMember,
        IReadOnlyList<SourceLocation> sites,
        string? detail)
    {
        Kind = kind;
        Source = source;
        Target = target;
        Subject = subject;
        Member = member;
        SubjectMember = subjectMember;
        Sites = sites;
        Detail = detail;
    }

    /// <summary>The violation kind.</summary>
    public ViolationKind Kind { get; }

    /// <summary>
    ///     The referencing type (Reference kind — for the inbound verbs this is where the edit happens), the
    ///     constructing type (Construction kind), the injecting type (Injection kind), the catching type
    ///     (Catch kind), the throwing type (Throw kind), or the exposing type (Expose kind).
    /// </summary>
    public TypeNode? Source { get; }

    /// <summary>
    ///     The referenced type (Reference kind), the constructed type (Construction kind), the injected
    ///     parameter type (Injection kind), the caught exception type (Catch kind), the thrown exception
    ///     type (Throw kind), or the exposed type (Expose kind).
    /// </summary>
    public TypeNode? Target { get; }

    /// <summary>The offending subject type (Shape kind).</summary>
    public TypeNode? Subject { get; }

    /// <summary>The banned member the source used (MemberUse kind); null otherwise.</summary>
    public MemberReference? Member { get; }

    /// <summary>The offending declared member (MemberShape kind); null otherwise.</summary>
    public MemberNode? SubjectMember { get; }

    /// <summary>The reference or declaration sites carrying the violation; empty for EmptySubject/RuleError.</summary>
    public IReadOnlyList<SourceLocation> Sites { get; }

    /// <summary>Free text for EmptySubject/RuleError; null otherwise.</summary>
    public string? Detail { get; }

    /// <summary>
    ///     This violation's deterministic within-rule report order key: (Source|Subject FullName, Target
    ///     FullName, Member SymbolId), compared ordinal by the checker. A MemberUse mirrors Reference's
    ///     (source, target) as (source FullName, member SymbolId); a MemberShape mirrors Shape's subject
    ///     as (declaring-type FullName, member SymbolId).
    /// </summary>
    internal (string Primary, string Secondary, string Tertiary) OrderKey
    {
        get
        {
            // A MemberShape's declaring-type FullName is the only primary key that is not a Source or a
            // Subject; every other kind leaves SubjectMember null and never reaches it.
            string primary = (Source ?? Subject)?.FullName
                             ?? (SubjectMember is { } member ? member.DeclaringTypeFullName : string.Empty);
            string secondary = Target?.FullName ?? string.Empty;
            string tertiary = Member?.SymbolId ?? SubjectMember?.SymbolId ?? string.Empty;
            return (primary, secondary, tertiary);
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
            _ => null
        };
    }

    internal static Violation Reference(TypeNode source, TypeNode target, IReadOnlyList<SourceLocation> sites)
    {
        return Edge(ViolationKind.Reference, source, target, sites);
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
        return new Violation(ViolationKind.MemberUse, source, null, null, member, null, sites, null);
    }

    internal static Violation Shape(TypeNode subject, IReadOnlyList<SourceLocation> sites)
    {
        return new Violation(ViolationKind.Shape, null, null, subject, null, null, sites, null);
    }

    internal static Violation MemberShape(MemberNode subjectMember, IReadOnlyList<SourceLocation> sites)
    {
        return new Violation(ViolationKind.MemberShape, null, null, null, null, subjectMember, sites, null);
    }

    internal static Violation EmptySubject(string detail)
    {
        return new Violation(ViolationKind.EmptySubject, null, null, null, null, null, Array.Empty<SourceLocation>(), detail);
    }

    internal static Violation RuleError(string detail)
    {
        return new Violation(ViolationKind.RuleError, null, null, null, null, null, Array.Empty<SourceLocation>(), detail);
    }

    // The one constructor call the six edge factories share: an edge violation is a (source, target) pair
    // with its sites, and every other slot empty.
    private static Violation Edge(
        ViolationKind kind, TypeNode source, TypeNode target, IReadOnlyList<SourceLocation> sites)
    {
        return new Violation(kind, source, target, null, null, null, sites, null);
    }
}
