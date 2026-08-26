namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.Packable()</c> premodifies the subject head: "packable projects", "packable project `A`"
///     (GRAMMAR §4.10, §6). It narrows to the projects an evaluation reported as producing a package.
///     Prefixing rather than substituting is what lets it compose with a
///     <see cref="ProjectNamedAdjective" /> head, and what keeps the fact attached to the noun it narrows
///     when the selection renders in reference position.
/// </summary>
/// <remarks>
///     Known-true only: a project whose <c>IsPackable</c> was never evaluated is not admitted, because
///     unknown is not a claim either way. That is the same tri-state honesty the packaging verbs hold to,
///     read from the other side — a verb passes what it does not know, and this adjective excludes it.
/// </remarks>
internal sealed class ProjectPackableAdjective : ProjectAdjective
{
    internal override AdjectivePlacement Placement => AdjectivePlacement.HeadPrefix;

    internal override string Fragment => "packable ";
}
