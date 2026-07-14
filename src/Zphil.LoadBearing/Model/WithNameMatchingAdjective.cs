using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary><c>.WithNameMatching("*Repo*")</c> → " whose name matches `*Repo*`" (GRAMMAR §5.2).</summary>
internal sealed class WithNameMatchingAdjective(string glob) : SelectionAdjective
{
    internal string Glob { get; } = glob;

    internal override AdjectivePlacement Placement => AdjectivePlacement.Inline;

    internal override string Fragment => $" whose name matches {ProseFormat.Backtick(Glob)}";
}