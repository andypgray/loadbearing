namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     One type throwing an exception type: a <c>throw</c> in <see cref="Source" />'s own source throws
///     something whose declared type is <see cref="Thrown" />. Read them from
///     <see cref="CodebaseModel.ThrowEdges" />. Throw expressions count as well as statements —
///     <c>?? throw</c>, a conditional or switch-expression arm, an expression-bodied
///     <c>=&gt; throw new X()</c>.
/// </summary>
/// <remarks>
///     <para>
///         What is recorded is the thrown expression's declared type, so
///         <c>catch (Exception ex) { ...; throw ex; }</c> is a throw of <c>System.Exception</c> whatever was
///         caught. A bare <c>throw;</c> introduces no expression and records nothing;
///         <c>throw null</c> and a throw of a type parameter record nothing either; a type throwing itself
///         gives nothing; and exception types are recorded at their definition. Both ends are the very type
///         instances <see cref="CodebaseModel.Types" /> lists, so compare them by reference rather than by
///         name.
///     </para>
///     <para>
///         A throw helper is not a throw: <c>ArgumentNullException.ThrowIfNull(x)</c> is an ordinary call,
///         so it appears as a <see cref="MemberEdge" /> and never here. A <c>throw new X()</c> appears here
///         and as a <see cref="ConstructorEdge" /> and as an ordinary reference — one place in the source,
///         three facts.
///     </para>
/// </remarks>
public sealed class ThrowEdge
{
    internal ThrowEdge(TypeNode source, TypeNode thrown, IReadOnlyList<SourceLocation> sites)
    {
        Source = source;
        Thrown = thrown;
        Sites = sites;
    }

    /// <summary>
    ///     Gets the throwing type. A <c>throw</c> written inside a lambda or a local function counts for the
    ///     type that encloses it, and one in top-level statements for <c>Program</c>.
    /// </summary>
    public TypeNode Source { get; }

    /// <summary>Gets the thrown expression's declared type.</summary>
    public TypeNode Thrown { get; }

    /// <summary>
    ///     Gets each distinct throw position, ordered by file then line. Two throws of the same type on one line
    ///     count as one site.
    /// </summary>
    public IReadOnlyList<SourceLocation> Sites { get; }
}
