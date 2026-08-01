using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.MustImplement(typeof(IDisposable))</c> → "must implement `IDisposable`" (GRAMMAR §5.3). The
///     anchor is a <see cref="TypeAnchor" />, so the string form renders the identical verb phrase.
/// </summary>
internal sealed class MustImplementConstraint(Selection subject, TypeAnchor anchor) : Constraint(subject)
{
    /// <summary>The interface the subject must implement, by <c>typeof</c> or by definition name.</summary>
    internal TypeAnchor Anchor { get; } = anchor;

    internal override string VerbPhrase => "must implement " + ProseFormat.Backtick(ProseFormat.AnchorName(Anchor));
}
