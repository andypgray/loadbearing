namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     A directed constructor-injection edge <c>Source → Injected</c>: <see cref="Source" />'s declared
///     instance constructor takes a parameter of <see cref="Injected" />'s type (GRAMMAR §4.7). It is a
///     <em>declaration-side</em> fact — the edge exists because the parameter is declared, whether or not
///     any constructor body dereferences it. <b>Primary constructors are included</b> (their parameters are
///     the modern injection surface). Parameter types decompose definition-level like type edges (§4.1):
///     <c>IEnumerable&lt;IFoo&gt;</c> yields edges to both <c>IEnumerable&lt;&gt;</c> and <c>IFoo</c>, an
///     array yields its element type, and a constructed generic yields the definition and every argument.
///     <see cref="Sites" /> lists the distinct <c>file:line</c> positions of the injected parameters,
///     deduped by (file, line).
/// </summary>
/// <remarks>
///     <see cref="Source" /> and <see cref="Injected" /> are the same <see cref="TypeNode" /> instances
///     held by <see cref="CodebaseModel.Types" /> (reference equality, not just name equality), so an
///     external injected type (e.g. an interface only a NuGet package declares) is a matchable target like
///     any other external endpoint. Self-injection (a type injecting itself) is never produced — the
///     injection analog of the reference-edge self-drop (§4.1). Enum and delegate source types declare no
///     walkable instance constructors and contribute no edges. Injection edges are recorded <em>beside</em>
///     the type-level edge, never instead of it: a constructor parameter of type <c>Foo</c> also mints a
///     <see cref="ReferenceEdge" /> to <c>Foo</c>.
/// </remarks>
public sealed class InjectionEdge
{
    internal InjectionEdge(TypeNode source, TypeNode injected, IReadOnlyList<SourceLocation> sites)
    {
        Source = source;
        Injected = injected;
        Sites = sites;
    }

    /// <summary>The injecting type — the type whose constructor takes the parameter.</summary>
    public TypeNode Source { get; }

    /// <summary>The injected parameter type.</summary>
    public TypeNode Injected { get; }

    /// <summary>The distinct injected-parameter sites, ordered by (file, line).</summary>
    public IReadOnlyList<SourceLocation> Sites { get; }
}