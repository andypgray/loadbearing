namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     One type using a member of another: <see cref="Source" />'s own source invokes, reads, writes or
///     subscribes to <see cref="Member" />. Read them from <see cref="CodebaseModel.MemberEdges" />.
/// </summary>
/// <remarks>
///     <para>
///         A method call, a method group handed over as a delegate, a property, field or event access
///         (<c>?.</c>, compound assignment and <c>+=</c> / <c>-=</c> included), and a bare name brought into
///         scope by <c>using static</c> all count. A type using its own members gives nothing. Both
///         <see cref="Source" /> and <see cref="MemberReference.ContainingType" /> are the very type
///         instances <see cref="CodebaseModel.Types" /> lists, so compare them by reference rather than by
///         name. The same use is also recorded as an ordinary reference to the member's declaring type in
///         <see cref="CodebaseModel.Edges" />, never instead of it.
///     </para>
///     <para>
///         Three uses are deliberately absent, so a rule reading these edges will not see them: a
///         <c>nameof</c> operand, which reads nothing at run time; an indexer; and a member the compiler
///         takes from a pattern rather than from a name the source spells — <c>await</c>'s
///         <c>GetAwaiter</c>, <c>using</c>'s <c>Dispose</c>, <c>foreach</c>'s enumerator, query syntax.
///     </para>
/// </remarks>
public sealed class MemberEdge
{
    internal MemberEdge(TypeNode source, MemberReference member, IReadOnlyList<SourceLocation> sites)
    {
        Source = source;
        Member = member;
        Sites = sites;
    }

    /// <summary>Gets the type whose own source contains the use.</summary>
    public TypeNode Source { get; }

    /// <summary>Gets the member that was used.</summary>
    public MemberReference Member { get; }

    /// <summary>
    ///     Gets each distinct place the use occurs, ordered by file then line. Two uses of the same member on
    ///     one line count as one site.
    /// </summary>
    public IReadOnlyList<SourceLocation> Sites { get; }
}
