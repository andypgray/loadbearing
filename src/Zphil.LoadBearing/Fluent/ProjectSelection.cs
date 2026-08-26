using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing.Fluent;

/// <summary>
///     An immutable, reusable selection over the <em>projects</em> the solution declares (GRAMMAR §4.10).
///     Minted by <see cref="Arch.Projects" /> and refined by the project adjectives
///     (<c>.Named</c>/<c>.Matching</c>/<c>.Packable</c>/<c>.Except</c>/<c>.Where</c>).
/// </summary>
/// <remarks>
///     A closed class hierarchy with a <c>private protected</c> constructor, <b>disjoint</b> from both
///     <see cref="Selection" /> and <see cref="MemberSelection" /> — that disjointness is what keeps the
///     shared adjective and verb names from colliding on overload resolution: a call binds to the type-,
///     member- or project-side vocabulary purely by receiver type.
///     Unlike a member selection there is no underlying type selection to borrow an owner from — a
///     project is not a set of types here, it is the build artifact itself — so the selection carries
///     <see cref="Owner" /> directly, which is what the foreign-<see cref="Arch" /> walk reads. Project
///     modal-verb extensions turn it into a terminal <see cref="Constraint" />; project adjectives clone
///     via <see cref="Rebuild" />, so a selection can be reused and refined in different directions.
/// </remarks>
public abstract class ProjectSelection
{
    private protected ProjectSelection(Arch owner, IReadOnlyList<ProjectAdjective> adjectives)
    {
        Owner = owner;
        Adjectives = adjectives;
    }

    /// <summary>The <see cref="Arch" /> this selection was minted on (GRAMMAR §3.2 fresh-instance contract).</summary>
    internal Arch Owner { get; }

    /// <summary>The ordered project-adjective refinements applied to the all-projects head.</summary>
    internal IReadOnlyList<ProjectAdjective> Adjectives { get; }

    /// <summary>
    ///     The clone each concrete project selection returns carrying a new adjective list — the hook the
    ///     adjective extensions build on, so a refinement never mutates the selection it refines.
    /// </summary>
    private protected abstract ProjectSelection Rebuild(IReadOnlyList<ProjectAdjective> adjectives);

    /// <summary>Appends one adjective and clones (the assembly-internal entry the adjective extensions call).</summary>
    internal ProjectSelection Refined(ProjectAdjective adjective)
    {
        var adjectives = new List<ProjectAdjective>(Adjectives) { adjective };
        return Rebuild(adjectives);
    }
}
