using Zphil.LoadBearing.Fluent;
using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.MustOnlyTarget(tfm, …)</c> → "must target only {list}" (GRAMMAR §4.10). STRICT, and for the
///     same reason <see cref="MustOnlyThrowConstraint" /> is: there is NO exemption caveat, because the
///     universe a project's target frameworks are drawn from is closed — every moniker it declares is one
///     this rule judges. The caveat's absence IS the strictness rendering; do not add a parenthetical.
/// </summary>
internal sealed class MustOnlyTargetConstraint(ProjectSelection subject, IReadOnlyList<string> frameworks)
    : ProjectConstraint(subject)
{
    /// <summary>The permitted target framework monikers, in authoring order.</summary>
    internal IReadOnlyList<string> Frameworks { get; } = frameworks;

    internal override string VerbPhrase =>
        "must target only " + ProseFormat.JoinReferences(Frameworks.Select(ProseFormat.Backtick).ToList());
}
