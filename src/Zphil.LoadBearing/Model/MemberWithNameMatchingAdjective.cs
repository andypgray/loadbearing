using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.WithNameMatching("*Handler*")</c> on a member selection → " whose name matches `*Handler*`" (GRAMMAR
///     §5.7).
/// </summary>
internal sealed class MemberWithNameMatchingAdjective(string glob) : MemberAdjective
{
    internal string Glob { get; } = glob;

    internal override AdjectivePlacement Placement => AdjectivePlacement.Inline;

    internal override string Fragment => $" whose name matches {ProseFormat.Backtick(Glob)}";
}