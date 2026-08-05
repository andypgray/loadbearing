using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.Methods.AttributedWith(typeof(McpServerToolAttribute))</c> premodifies the member head:
///     "`[McpServerTool]`-attributed methods of types in `Zphil.LoadBearing.*`" (GRAMMAR §5.7, §6). The
///     anchor is an <see cref="TypeAnchor" />, so the string form renders the identical prefix.
///     <para>
///         <b>Why a head prefix rather than the type side's inline " attributed with" clause.</b> A member
///         subject renders its type selection in reference position, so an inline member fragment would land
///         after that reference — and <c>arch.Types.AttributedWith(X).Methods</c> would render
///         <em>byte-identically</em> to <c>arch.Types.Methods.AttributedWith(X)</c> while naming a different
///         subject (every method of an attributed type, versus the attributed methods of any type). The
///         member <em>naming</em> adjectives already collide that way by design; here both attachment sites
///         are live in this repository's own spec, so the garden path is real rather than theoretical.
///         Prefixing keeps the fact attached to the noun it narrows, exactly as
///         <see cref="AuthoredAdjective" /> does for the same reference-position ambiguity.
///     </para>
/// </summary>
internal sealed class MemberAttributedWithAdjective(TypeAnchor anchor) : MemberAdjective
{
    /// <summary>The attribute the member must carry, by <c>typeof</c> or by definition name.</summary>
    internal TypeAnchor Anchor { get; } = anchor;

    internal override AdjectivePlacement Placement => AdjectivePlacement.HeadPrefix;

    internal override string Fragment => ProseFormat.Backtick(ProseFormat.AttributeName(Anchor)) + "-attributed ";
}
