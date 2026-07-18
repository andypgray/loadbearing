using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Checking;

/// <summary>
///     One concrete way a rule is broken (GRAMMAR §4.3). Which slots are populated is governed by
///     <see cref="Kind" />: a <see cref="ViolationKind.Reference" /> carries <see cref="Source" /> and
///     <see cref="Target" /> with the edge's reference <see cref="Sites" />; a
///     <see cref="ViolationKind.MemberUse" /> carries <see cref="Source" /> and <see cref="Member" />
///     with the member-use edge's <see cref="Sites" />; a <see cref="ViolationKind.Shape" /> carries
///     <see cref="Subject" /> with its declaration sites; a <see cref="ViolationKind.MemberShape" />
///     carries <see cref="SubjectMember" /> with its declaration sites;
///     <see cref="ViolationKind.EmptySubject" /> and <see cref="ViolationKind.RuleError" /> carry only
///     <see cref="Detail" />.
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

    /// <summary>The referencing type (Reference kind — for the inbound verbs this is where the edit happens).</summary>
    public TypeNode? Source { get; }

    /// <summary>The referenced type (Reference kind).</summary>
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
    ///     This violation's stable baseline identity (GRAMMAR §4.3): an edge key
    ///     (<see cref="Source" />, <see cref="Target" /> symbol IDs) for a Reference, an edge key
    ///     (<see cref="Source" /> symbol ID, <see cref="Member" />'s member DocId) for a MemberUse, a
    ///     subject key for a Shape, and a member-subject key (<see cref="SubjectMember" />'s member DocId)
    ///     for a MemberShape (GRAMMAR §4.6). <see cref="ViolationKind.EmptySubject" /> and
    ///     <see cref="ViolationKind.RuleError" /> have no stable identity and return null — they can
    ///     never be grandfathered. Shared by the checker's ratchet and the <c>baseline</c> command's capture.
    /// </summary>
    public BaselineEntry? BaselineIdentity()
    {
        return Kind switch
        {
            ViolationKind.Reference => BaselineEntry.ForEdge(Source!.SymbolId, Target!.SymbolId),
            ViolationKind.MemberUse => BaselineEntry.ForEdge(Source!.SymbolId, Member!.SymbolId),
            ViolationKind.Shape => BaselineEntry.ForSubject(Subject!.SymbolId),
            ViolationKind.MemberShape => BaselineEntry.ForSubject(SubjectMember!.SymbolId),
            _ => null
        };
    }

    internal static Violation Reference(TypeNode source, TypeNode target, IReadOnlyList<SourceLocation> sites)
    {
        return new Violation(ViolationKind.Reference, source, target, null, null, null, sites, null);
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
}