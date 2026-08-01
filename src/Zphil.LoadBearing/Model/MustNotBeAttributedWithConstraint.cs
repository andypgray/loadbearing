using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.MustNotBeAttributedWith(typeof(TableAttribute), …)</c> → "must not be attributed with
///     `[Table]`" (GRAMMAR §5.3) — none-of over the anchor list: a subject violates iff it carries
///     ANY anchor attribute. Each anchor is <c>Attribute</c>-stripped and bracketed like the positive
///     verb. Anchors are stored as raw <see cref="AttributeAnchor" />s on the node, never selection
///     operands (GRAMMAR §10); one list mixes no forms, because the overloads are homogeneous —
///     all <c>typeof</c> or all definition names.
/// </summary>
internal sealed class MustNotBeAttributedWithConstraint(Selection subject, IReadOnlyList<AttributeAnchor> anchors) : Constraint(subject)
{
    /// <summary>The attributes the subject must not carry (none-of).</summary>
    internal IReadOnlyList<AttributeAnchor> Anchors { get; } = anchors;

    internal override string VerbPhrase => "must not be attributed with " + ProseFormat.AttributeList(Anchors);
}