using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.WithSuffix("Async")</c> on a member selection → " named `*Async`" (GRAMMAR §5.7; reuses the §5.2
///     fragment).
/// </summary>
internal sealed class MemberWithSuffixAdjective(string suffix) : MemberAdjective
{
    internal string Suffix { get; } = suffix;

    internal override AdjectivePlacement Placement => AdjectivePlacement.Inline;

    internal override string Fragment => $" named {ProseFormat.Backtick("*" + Suffix)}";
}