namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.Authored()</c> premodifies the subject head: "authored types", "authored interfaces"
///     (GRAMMAR §5.2, §6). It narrows to the types no source generator emitted — the opt-out from the
///     §4.1 boundary that puts generator output inside a project noun. Prefixing rather than
///     substituting is what lets it compose with an <see cref="OfKindAdjective" /> head, and what keeps
///     the fact attached to the noun it narrows when the selection renders in reference position.
/// </summary>
internal sealed class AuthoredAdjective : SelectionAdjective
{
    internal override AdjectivePlacement Placement => AdjectivePlacement.HeadPrefix;

    internal override string Fragment => "authored ";
}