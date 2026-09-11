namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     One type taking another as a constructor parameter: an instance constructor <see cref="Source" />
///     declares has a parameter of <see cref="Injected" />'s type. Read them from
///     <see cref="CodebaseModel.InjectionEdges" />. The parameter being declared is the whole fact, whether
///     or not any constructor body does anything with it, and a primary constructor's parameters count.
/// </summary>
/// <remarks>
///     Parameter types are recorded at their definition and taken apart: <c>IEnumerable&lt;IFoo&gt;</c>
///     gives an edge to <c>IEnumerable&lt;&gt;</c> and another to <c>IFoo</c>, and an array gives its
///     element type. A type injecting itself gives nothing; the compiler's default parameterless
///     constructor, a record's copy constructor and static constructors declare nothing to record; and enums
///     and delegates contribute none. An injected type only a package or the framework declares is in
///     <see cref="CodebaseModel.Types" /> like any other, so a rule can name it. Both ends are the very type
///     instances that list holds, so compare them by reference rather than by name, and the same parameter
///     is also recorded as an ordinary reference in <see cref="CodebaseModel.Edges" />, never instead of it.
/// </remarks>
public sealed class InjectionEdge
{
    internal InjectionEdge(TypeNode source, TypeNode injected, IReadOnlyList<SourceLocation> sites)
    {
        Source = source;
        Injected = injected;
        Sites = sites;
    }

    /// <summary>Gets the injecting type — the one whose constructor declares the parameter.</summary>
    public TypeNode Source { get; }

    /// <summary>Gets the parameter's type.</summary>
    public TypeNode Injected { get; }

    /// <summary>
    ///     Gets each distinct place a parameter of this type is declared, ordered by file then line. Two such
    ///     parameters on one line count as one site.
    /// </summary>
    public IReadOnlyList<SourceLocation> Sites { get; }
}
