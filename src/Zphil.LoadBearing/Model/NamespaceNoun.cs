using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     Types in a namespace glob — <c>arch.Namespace("MyApp.Legacy.Billing.*")</c>. Reference
///     fragment: "types in `MyApp.Legacy.Billing.*`" (GRAMMAR §5.1).
/// </summary>
internal sealed class NamespaceNoun(string glob) : SelectionNoun
{
    /// <summary>The namespace glob (dot-segment aware — see <see cref="NamespacePattern" />).</summary>
    internal string Glob { get; } = glob;

    internal override string Locative => $" in {ProseFormat.Backtick(Glob)}";

    internal override string CollapsedLocative(IReadOnlyList<SelectionNoun> group)
    {
        var globs = group.Select(noun => ProseFormat.Backtick(((NamespaceNoun)noun).Glob)).ToList();
        return $" in {ProseFormat.JoinReferences(globs)}";
    }
}
