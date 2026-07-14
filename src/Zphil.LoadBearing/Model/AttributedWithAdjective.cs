using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.AttributedWith(typeof(ApiControllerAttribute))</c> → " attributed with `[ApiController]`"
///     (GRAMMAR §5.2) — the <c>Attribute</c> suffix is stripped and the name bracketed.
/// </summary>
internal sealed class AttributedWithAdjective(Type type) : SelectionAdjective
{
    internal Type Type { get; } = type;

    internal override AdjectivePlacement Placement => AdjectivePlacement.Inline;

    internal override string Fragment => $" attributed with {ProseFormat.Backtick(ProseFormat.AttributeName(Type))}";
}