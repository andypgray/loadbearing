using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.DerivedFrom(typeof(ControllerBase))</c> → " derived from `ControllerBase`" (GRAMMAR §5.2).
///     The anchor is a <see cref="TypeAnchor" />, so the string form
///     (<c>.DerivedFrom("Microsoft.AspNetCore.Mvc.ControllerBase")</c>) renders the identical fragment.
/// </summary>
internal sealed class DerivedFromAdjective(TypeAnchor anchor) : SelectionAdjective
{
    /// <summary>The base type the subject must derive from, by <c>typeof</c> or by definition name.</summary>
    internal TypeAnchor Anchor { get; } = anchor;

    internal override AdjectivePlacement Placement => AdjectivePlacement.Inline;

    internal override string Fragment => $" derived from {ProseFormat.Backtick(ProseFormat.AnchorName(Anchor))}";
}
