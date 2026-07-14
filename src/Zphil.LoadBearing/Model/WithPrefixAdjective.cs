using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary><c>.WithPrefix("Legacy")</c> → " named `Legacy*`" (GRAMMAR §5.2).</summary>
internal sealed class WithPrefixAdjective(string prefix) : SelectionAdjective
{
    internal string Prefix { get; } = prefix;

    internal override AdjectivePlacement Placement => AdjectivePlacement.Inline;

    internal override string Fragment => $" named {ProseFormat.Backtick(Prefix + "*")}";
}