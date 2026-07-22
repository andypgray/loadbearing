using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.MustNotImplement(typeof(IHandler&lt;&gt;), …)</c> → "must not implement `IHandler&lt;T&gt;`"
///     (GRAMMAR §5.3) — none-of over the anchor list: a subject violates iff it implements ANY anchor.
///     Anchors are stored as raw <see cref="Type" />s on the node (the hierarchy-verb shape), never
///     selection operands (GRAMMAR §10).
/// </summary>
internal sealed class MustNotImplementConstraint(Selection subject, IReadOnlyList<Type> types) : Constraint(subject)
{
    /// <summary>The interfaces the subject must not implement (none-of).</summary>
    internal IReadOnlyList<Type> Types { get; } = types;

    internal override string VerbPhrase => "must not implement " + ProseFormat.TypeList(Types);
}