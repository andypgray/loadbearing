using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Checking;

/// <summary>
///     One concrete way a rule is broken (GRAMMAR §4.3). Which slots are populated is governed by
///     <see cref="Kind" />: a <see cref="ViolationKind.Reference" /> carries <see cref="Source" /> and
///     <see cref="Target" /> with the edge's reference <see cref="Sites" />; a
///     <see cref="ViolationKind.Shape" /> carries <see cref="Subject" /> with its declaration sites;
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
        IReadOnlyList<SourceLocation> sites,
        string? detail)
    {
        Kind = kind;
        Source = source;
        Target = target;
        Subject = subject;
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

    /// <summary>The reference or declaration sites carrying the violation; empty for EmptySubject/RuleError.</summary>
    public IReadOnlyList<SourceLocation> Sites { get; }

    /// <summary>Free text for EmptySubject/RuleError; null otherwise.</summary>
    public string? Detail { get; }

    /// <summary>
    ///     This violation's stable baseline identity (GRAMMAR §4.3): an edge key
    ///     (<see cref="Source" />, <see cref="Target" /> symbol IDs) for a Reference, a subject key for
    ///     a Shape. <see cref="ViolationKind.EmptySubject" /> and <see cref="ViolationKind.RuleError" />
    ///     have no stable identity and return null — they can never be grandfathered. Shared by the
    ///     checker's ratchet and the <c>baseline</c> command's capture.
    /// </summary>
    public BaselineEntry? BaselineIdentity()
    {
        return Kind switch
        {
            ViolationKind.Reference => BaselineEntry.ForEdge(Source!.SymbolId, Target!.SymbolId),
            ViolationKind.Shape => BaselineEntry.ForSubject(Subject!.SymbolId),
            _ => null
        };
    }

    internal static Violation Reference(TypeNode source, TypeNode target, IReadOnlyList<SourceLocation> sites)
    {
        return new Violation(ViolationKind.Reference, source, target, null, sites, null);
    }

    internal static Violation Shape(TypeNode subject, IReadOnlyList<SourceLocation> sites)
    {
        return new Violation(ViolationKind.Shape, null, null, subject, sites, null);
    }

    internal static Violation EmptySubject(string detail)
    {
        return new Violation(ViolationKind.EmptySubject, null, null, null, Array.Empty<SourceLocation>(), detail);
    }

    internal static Violation RuleError(string detail)
    {
        return new Violation(ViolationKind.RuleError, null, null, null, Array.Empty<SourceLocation>(), detail);
    }
}