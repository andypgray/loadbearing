namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     A directed exposure edge <c>Source → Exposed</c>: <see cref="Source" /> names <see cref="Exposed" /> in a
///     public <em>signature position</em> — a method's return or parameter type, or a property/field/event type —
///     of an effectively-public member (GRAMMAR §4.9). It is minted only from a public member whose containing-type
///     chain is public at every level, so a <c>public</c> member nested in an <c>internal</c> type surfaces nothing
///     (the honesty boundary: an internal member is not surface). Signature types decompose definition-level like
///     type edges (§4.1): <c>Task&lt;Order&gt;</c> yields edges to both <c>Task&lt;&gt;</c> and <c>Order</c>, an
///     array yields its element type, a tuple yields the open definition (recorded under its display form
///     <c>(T1, T2)</c>, not <c>System.ValueTuple&lt;T1, T2&gt;</c>) and every element type, and <c>int?</c>
///     yields <c>System.Nullable&lt;T&gt;</c> and <c>System.Int32</c>.
///     <see cref="Sites" /> lists the distinct <c>file:line</c> positions of the exposing members (their
///     declaration lines), deduped by (file, line).
/// </summary>
/// <remarks>
///     <see cref="Source" /> and <see cref="Exposed" /> are the same <see cref="TypeNode" /> instances held by
///     <see cref="CodebaseModel.Types" /> (reference equality, not just name equality), so an external exposed
///     type (e.g. a <c>DataTable</c> only the BCL declares) is a matchable target like any other external
///     endpoint. Self-exposure (a type naming itself in its own signature) is never produced — the exposure
///     analog of the reference-edge self-drop (§4.1), which also self-drops the enum-value-self-typing case.
///     Constructor parameters are the injection axis's (§4.7), not this one's, and base-type/interface lists are
///     inheritance (§5.2), not members — neither mints an exposure edge. Where the signature textually names its
///     type, the exposure edge is recorded <em>beside</em> the type-level <see cref="ReferenceEdge" /> that
///     type-name syntax mints, never instead of it — one site, two facts. A spelling that names no type (tuple
///     and <c>?</c> syntax, whose wrappers decomposition synthesizes; predefined keywords like <c>int</c>) mints
///     no twin: the exposure edge stands alone there by design.
/// </remarks>
public sealed class ExposureEdge
{
    internal ExposureEdge(TypeNode source, TypeNode exposed, IReadOnlyList<SourceLocation> sites)
    {
        Source = source;
        Exposed = exposed;
        Sites = sites;
    }

    /// <summary>The exposing type — the type whose public member names the exposed type in a signature position.</summary>
    public TypeNode Source { get; }

    /// <summary>The exposed type.</summary>
    public TypeNode Exposed { get; }

    /// <summary>The distinct exposing-member sites, ordered by (file, line).</summary>
    public IReadOnlyList<SourceLocation> Sites { get; }
}