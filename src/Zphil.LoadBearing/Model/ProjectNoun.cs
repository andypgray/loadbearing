using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     Types in a named project — <c>arch.Project("MyApp.Web")</c>. Reference fragment:
///     "types in project `MyApp.Web`" (GRAMMAR §5.1).
/// </summary>
internal sealed class ProjectNoun(string name) : SelectionNoun
{
    /// <summary>The project (assembly) name.</summary>
    internal string Name { get; } = name;

    internal override string Locative => $" in project {ProseFormat.Backtick(Name)}";
}