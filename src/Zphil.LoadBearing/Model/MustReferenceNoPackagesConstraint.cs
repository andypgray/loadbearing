using Zphil.LoadBearing.Fluent;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.MustReferenceNoPackages()</c> → "must reference no NuGet packages (declared references only;
///     transitive dependencies are not seen)" (GRAMMAR §4.10). The parenthetical is the honesty boundary,
///     pinned exactly as <see cref="MustOnlyReferenceConstraint" />'s is: the model holds what a project
///     declares, so a project that takes its whole dependency surface transitively through a project
///     reference satisfies this verb and the sentence says so rather than letting a reader assume
///     otherwise.
/// </summary>
internal sealed class MustReferenceNoPackagesConstraint(ProjectSelection subject) : ProjectConstraint(subject)
{
    internal override string VerbPhrase =>
        "must reference no NuGet packages (declared references only; transitive dependencies are not seen)";
}
