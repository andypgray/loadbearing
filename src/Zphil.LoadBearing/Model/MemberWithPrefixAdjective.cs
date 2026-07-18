using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary><c>.WithPrefix("Get")</c> on a member selection → " named `Get*`" (GRAMMAR §5.7; reuses the §5.2 fragment).</summary>
internal sealed class MemberWithPrefixAdjective(string prefix) : MemberAdjective
{
    internal string Prefix { get; } = prefix;

    internal override AdjectivePlacement Placement => AdjectivePlacement.Inline;

    internal override string Fragment => $" named {ProseFormat.Backtick(Prefix + "*")}";
}