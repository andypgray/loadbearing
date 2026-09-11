namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     One type naming another in its public API: a member of <see cref="Source" /> names
///     <see cref="Exposed" /> in a signature position — a method's return type or one of its parameter
///     types, or a property, field or event's type. Read them from
///     <see cref="CodebaseModel.ExposureEdges" />.
/// </summary>
/// <remarks>
///     <para>
///         Only what a caller outside the assembly could reach counts: the member must be public and every
///         type enclosing it public too, so a <c>public</c> member of an <c>internal</c> type exposes
///         nothing. A type naming itself gives nothing. A constructor's parameters belong to
///         <see cref="CodebaseModel.InjectionEdges" /> instead, and base types and interface lists are
///         inheritance rather than signatures; neither appears here. Nor do indexers, operators,
///         conversions, accessors, explicit interface implementations, compiler-generated members, or a
///         <c>void</c> return, which names no type.
///     </para>
///     <para>
///         Signature types are recorded at their definition and taken apart: <c>Task&lt;Order&gt;</c> gives
///         edges to <c>Task&lt;&gt;</c> and <c>Order</c>, an array gives its element type,
///         <c>(Order, Widget)</c> gives the open tuple under its display form <c>(T1, T2)</c> plus both
///         element types, and <c>int?</c> gives <c>System.Nullable&lt;T&gt;</c> and <c>System.Int32</c>.
///         Framework types are recorded like any other, so <c>public string Name</c> exposes
///         <c>System.String</c>. Where the signature spells a type's name the edge sits beside the ordinary
///         reference that name produces; where it spells none — a tuple, a <c>?</c>, the keyword
///         <c>int</c> — the exposure edge stands alone.
///     </para>
///     <para>
///         What is recorded is the declared signature, never what flows through it: a member returning
///         <c>object</c> exposes <c>System.Object</c> and nothing else, whatever the caller casts it to.
///     </para>
/// </remarks>
public sealed class ExposureEdge
{
    internal ExposureEdge(TypeNode source, TypeNode exposed, IReadOnlyList<SourceLocation> sites)
    {
        Source = source;
        Exposed = exposed;
        Sites = sites;
    }

    /// <summary>Gets the exposing type — the one whose public member names the exposed type.</summary>
    public TypeNode Source { get; }

    /// <summary>Gets the type named in the signature.</summary>
    public TypeNode Exposed { get; }

    /// <summary>
    ///     Gets the declaration line of each exposing member, ordered by file then line. Two exposing members on
    ///     one line count as one site.
    /// </summary>
    public IReadOnlyList<SourceLocation> Sites { get; }
}
