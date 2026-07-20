namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     A directed construction edge <c>Source → Constructed</c>: <see cref="Source" />'s declaration source
///     directly creates <see cref="Constructed" /> with an object-creation expression — explicit
///     <c>new Foo()</c> or target-typed <c>new()</c> (GRAMMAR §4.5; the construction-edge semantics ratified
///     for Phase 18). Constructed generics normalize to their open definition (§4.1), so <c>new Box&lt;int&gt;()</c>
///     records an edge to <c>Box&lt;&gt;</c>. <see cref="Sites" /> lists the distinct <c>file:line</c> positions
///     where the construction occurs, deduped by (file, line).
/// </summary>
/// <remarks>
///     <see cref="Source" /> and <see cref="Constructed" /> are the same <see cref="TypeNode" /> instances
///     held by <see cref="CodebaseModel.Types" /> (reference equality, not just name equality).
///     Self-construction (a type constructing itself) is never produced — the construction analog of the
///     reference-edge self-drop (GRAMMAR §4.1). Construction edges are recorded <em>beside</em> the
///     type-level edge, never instead of it: every <c>new Foo()</c> also mints a <see cref="ReferenceEdge" />
///     to <see cref="Constructed" />. Delegate creation, attribute applications, <c>base(…)</c>/<c>this(…)</c>
///     initializers, <c>with</c> expressions, and array creation are excluded at extraction and never appear
///     here; reflection/container construction is a documented honesty boundary, not a recorded edge.
/// </remarks>
public sealed class ConstructorEdge
{
    internal ConstructorEdge(TypeNode source, TypeNode constructed, IReadOnlyList<SourceLocation> sites)
    {
        Source = source;
        Constructed = constructed;
        Sites = sites;
    }

    /// <summary>The constructing type.</summary>
    public TypeNode Source { get; }

    /// <summary>The constructed type.</summary>
    public TypeNode Constructed { get; }

    /// <summary>The distinct construction sites, ordered by (file, line).</summary>
    public IReadOnlyList<SourceLocation> Sites { get; }
}