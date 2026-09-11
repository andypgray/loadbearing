namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     One type creating another: <see cref="Source" />'s own source creates <see cref="Constructed" /> with
///     an object-creation expression, either <c>new Foo()</c> or a target-typed <c>new()</c>. Read them from
///     <see cref="CodebaseModel.ConstructorEdges" />.
/// </summary>
/// <remarks>
///     <para>
///         Types are recorded at their definition, so <c>new Box&lt;int&gt;()</c> gives an edge to
///         <c>Box&lt;&gt;</c>. A type creating itself gives nothing. Both ends are the very type instances
///         <see cref="CodebaseModel.Types" /> lists, so compare them by reference rather than by name, and
///         the same expression is also recorded as an ordinary reference in
///         <see cref="CodebaseModel.Edges" />, never instead of it.
///     </para>
///     <para>
///         Five spellings never appear here, none of them a creation of the type written: an attribute
///         applied to a declaration, a <c>: base(...)</c> or <c>: this(...)</c> initializer, delegate
///         creation (<c>new Action(M)</c> and its target-typed form), a <c>with</c> expression, and array
///         creation. An object a container or reflection creates is invisible either way — but a factory
///         lambda that itself writes <c>new</c>, as in <c>AddScoped(sp =&gt; new Foo(...))</c>, is an
///         ordinary creation and is recorded.
///     </para>
/// </remarks>
public sealed class ConstructorEdge
{
    internal ConstructorEdge(TypeNode source, TypeNode constructed, IReadOnlyList<SourceLocation> sites)
    {
        Source = source;
        Constructed = constructed;
        Sites = sites;
    }

    /// <summary>Gets the constructing type.</summary>
    public TypeNode Source { get; }

    /// <summary>Gets the type that is created.</summary>
    public TypeNode Constructed { get; }

    /// <summary>
    ///     Gets each distinct place the creation occurs, ordered by file then line. Two creations of the same
    ///     type on one line count as one site.
    /// </summary>
    public IReadOnlyList<SourceLocation> Sites { get; }
}
