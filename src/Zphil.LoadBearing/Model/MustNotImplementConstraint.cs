using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.MustNotImplement(typeof(IHandler&lt;&gt;), …)</c> → "must not implement `IHandler&lt;T&gt;`"
///     (GRAMMAR §5.3) — none-of over the anchor list: a subject violates iff it implements ANY anchor.
///     Anchors are stored as raw <see cref="TypeAnchor" />s on the node (the hierarchy-verb shape), never
///     selection operands (GRAMMAR §10); one list mixes no forms, because the overloads are homogeneous —
///     all <c>typeof</c> or all definition names.
/// </summary>
internal sealed class MustNotImplementConstraint(Selection subject, IReadOnlyList<TypeAnchor> anchors) : Constraint(subject)
{
    /// <summary>The interfaces the subject must not implement (none-of).</summary>
    internal IReadOnlyList<TypeAnchor> Anchors { get; } = anchors;

    internal override string VerbPhrase => "must not implement " + ProseFormat.AnchorList(Anchors);
}
