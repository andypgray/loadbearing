using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary><c>.WithSuffix("Controller")</c> → " named `*Controller`" (GRAMMAR §5.2).</summary>
internal sealed class WithSuffixAdjective(string suffix) : SelectionAdjective
{
    internal string Suffix { get; } = suffix;

    internal override AdjectivePlacement Placement => AdjectivePlacement.Inline;

    internal override string Fragment => $" named {ProseFormat.Backtick("*" + Suffix)}";
}