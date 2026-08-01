using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.MustDeriveFrom(typeof(ControllerBase))</c> → "must derive from `ControllerBase`"
///     (GRAMMAR §5.3). The anchor is a <see cref="TypeAnchor" />, so the string form renders the
///     identical verb phrase.
/// </summary>
internal sealed class MustDeriveFromConstraint(Selection subject, TypeAnchor anchor) : Constraint(subject)
{
    /// <summary>The base type the subject must derive from, by <c>typeof</c> or by definition name.</summary>
    internal TypeAnchor Anchor { get; } = anchor;

    internal override string VerbPhrase => "must derive from " + ProseFormat.Backtick(ProseFormat.AnchorName(Anchor));
}
