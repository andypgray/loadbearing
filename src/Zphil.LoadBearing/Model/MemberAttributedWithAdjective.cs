using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.Methods.AttributedWith(typeof(McpServerToolAttribute))</c> premodifies the member head:
///     "`[McpServerTool]`-attributed methods of types in `Zphil.LoadBearing.*`" (GRAMMAR §5.7, §6). The
///     anchor is a <see cref="TypeAnchor" />, so the string form renders the identical prefix.
/// </summary>
/// <remarks>
///     A head prefix, never the type side's inline clause: a member subject renders its type selection in
///     reference position, so an inline member fragment would land after that reference and
///     <c>arch.Types.AttributedWith(X).Methods</c> would render byte-identically to
///     <c>arch.Types.Methods.AttributedWith(X)</c> while naming a different subject. Prefixing keeps the
///     fact attached to the noun it narrows, as <see cref="AuthoredAdjective" /> does for the same ambiguity.
/// </remarks>
internal sealed class MemberAttributedWithAdjective(TypeAnchor anchor) : MemberAdjective
{
    /// <summary>The attribute the member must carry, by <c>typeof</c> or by definition name.</summary>
    internal TypeAnchor Anchor { get; } = anchor;

    internal override AdjectivePlacement Placement => AdjectivePlacement.HeadPrefix;

    internal override string Fragment => ProseFormat.Backtick(ProseFormat.AttributeName(Anchor)) + "-attributed ";
}
