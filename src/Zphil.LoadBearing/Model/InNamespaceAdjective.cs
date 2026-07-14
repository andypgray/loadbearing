using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary><c>.InNamespace("MyApp.Web.*")</c> → " in `MyApp.Web.*`" (GRAMMAR §5.2).</summary>
internal sealed class InNamespaceAdjective(string glob) : SelectionAdjective
{
    internal string Glob { get; } = glob;

    internal override AdjectivePlacement Placement => AdjectivePlacement.Inline;

    internal override string Fragment => $" in {ProseFormat.Backtick(Glob)}";
}