using Zphil.LoadBearing.Fluent;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     Every project selection there is: the bare all-projects head <see cref="Arch.Projects" /> mints
///     (with an empty adjective list) plus whatever the project adjectives have appended to it
///     (GRAMMAR §4.10). One concrete type is enough because the project stratum has no projections and no
///     kind-specific verbs — nothing for a second selection type to keep reachable.
/// </summary>
internal sealed class RefinedProjectSelection : ProjectSelection
{
    internal RefinedProjectSelection(Arch owner, IReadOnlyList<ProjectAdjective> adjectives)
        : base(owner, adjectives)
    {
    }

    private protected override ProjectSelection Rebuild(IReadOnlyList<ProjectAdjective> adjectives)
    {
        return new RefinedProjectSelection(Owner, adjectives);
    }
}
