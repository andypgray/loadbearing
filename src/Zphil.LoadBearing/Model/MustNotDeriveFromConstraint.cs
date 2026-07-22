using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.MustNotDeriveFrom(typeof(Exception), …)</c> → "must not derive from `Exception`"
///     (GRAMMAR §5.3) — none-of over the anchor list: a subject violates iff it derives from ANY
///     anchor. Anchors are stored as raw <see cref="Type" />s on the node, never selection operands
///     (GRAMMAR §10).
/// </summary>
internal sealed class MustNotDeriveFromConstraint(Selection subject, IReadOnlyList<Type> types) : Constraint(subject)
{
    /// <summary>The base types the subject must not derive from (none-of).</summary>
    internal IReadOnlyList<Type> Types { get; } = types;

    internal override string VerbPhrase => "must not derive from " + ProseFormat.TypeList(Types);
}