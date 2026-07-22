using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.MustNotBeAttributedWith(typeof(TableAttribute), …)</c> → "must not be attributed with
///     `[Table]`" (GRAMMAR §5.3) — none-of over the anchor list: a subject violates iff it carries
///     ANY anchor attribute. Each anchor is <c>Attribute</c>-stripped and bracketed like the positive
///     verb. Anchors are stored as raw <see cref="Type" />s on the node, never selection operands
///     (GRAMMAR §10).
/// </summary>
internal sealed class MustNotBeAttributedWithConstraint(Selection subject, IReadOnlyList<Type> types) : Constraint(subject)
{
    /// <summary>The attribute types the subject must not carry (none-of).</summary>
    internal IReadOnlyList<Type> Types { get; } = types;

    internal override string VerbPhrase => "must not be attributed with " + ProseFormat.AttributeList(Types);
}