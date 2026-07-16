namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     A directed member-use edge <c>Source → Member</c>: <see cref="Source" />'s declaration source
///     uses <see cref="Member" /> — a method invocation or method-group reference, a property/field/
///     event access (including <c>?.</c>, compound assignment, and <c>+=</c>/<c>-=</c> subscription),
///     or a <c>using static</c> bare name (GRAMMAR §4.5). <see cref="Sites" /> lists the distinct
///     <c>file:line</c> positions where the use occurs, deduped by (file, line).
/// </summary>
/// <remarks>
///     <see cref="Source" /> is the same <see cref="TypeNode" /> instance held by
///     <see cref="CodebaseModel.Types" /> (reference equality, not just name equality), and
///     <see cref="MemberReference.ContainingType" /> is likewise a shared node. Self-uses (a type using
///     its own member) are never produced — the member analog of the reference-edge self-drop
///     (GRAMMAR §4.1). Member edges are recorded <em>beside</em> the type-level edge, not instead of it:
///     the same use also mints a <see cref="ReferenceEdge" /> to <see cref="MemberReference.ContainingType" />.
/// </remarks>
public sealed class MemberEdge
{
    internal MemberEdge(TypeNode source, MemberReference member, IReadOnlyList<SourceLocation> sites)
    {
        Source = source;
        Member = member;
        Sites = sites;
    }

    /// <summary>The using type.</summary>
    public TypeNode Source { get; }

    /// <summary>The used member.</summary>
    public MemberReference Member { get; }

    /// <summary>The distinct use sites, ordered by (file, line).</summary>
    public IReadOnlyList<SourceLocation> Sites { get; }
}