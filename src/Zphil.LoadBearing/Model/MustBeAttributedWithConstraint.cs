using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.MustBeAttributedWith(typeof(ApiControllerAttribute))</c> → "must be attributed with
///     `[ApiController]`" (GRAMMAR §5.3) — <c>Attribute</c> suffix stripped, name bracketed. The anchor is
///     an <see cref="TypeAnchor" />, so the string form renders the identical verb phrase.
/// </summary>
internal sealed class MustBeAttributedWithConstraint(Selection subject, TypeAnchor anchor) : Constraint(subject)
{
    /// <summary>The attribute the subject must carry, by <c>typeof</c> or by definition name.</summary>
    internal TypeAnchor Anchor { get; } = anchor;

    internal override string VerbPhrase => "must be attributed with " + ProseFormat.Backtick(ProseFormat.AttributeName(Anchor));
}