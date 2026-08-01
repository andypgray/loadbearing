using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.AttributedWith(typeof(ApiControllerAttribute))</c> → " attributed with `[ApiController]`"
///     (GRAMMAR §5.2) — the <c>Attribute</c> suffix is stripped and the name bracketed. The anchor is an
///     <see cref="TypeAnchor" />, so the string form
///     (<c>.AttributedWith("MyPackage.ApiControllerAttribute")</c>) renders the identical fragment.
/// </summary>
internal sealed class AttributedWithAdjective(TypeAnchor anchor) : SelectionAdjective
{
    /// <summary>The attribute the subject must carry, by <c>typeof</c> or by definition name.</summary>
    internal TypeAnchor Anchor { get; } = anchor;

    internal override AdjectivePlacement Placement => AdjectivePlacement.Inline;

    internal override string Fragment => $" attributed with {ProseFormat.Backtick(ProseFormat.AttributeName(Anchor))}";
}