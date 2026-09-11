using Zphil.LoadBearing.Fluent;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     A refinement on a <see cref="ProjectSelection" /> — a closed hierarchy so each project adjective
///     owns its own prose fragment (GRAMMAR §2 admission rule, §4.10). Reuses
///     <see cref="AdjectivePlacement" />: the project stratum uses all four placements — <c>Named</c>
///     substitutes the head, <c>Packable</c> premodifies it, <c>Matching</c> is an inline reduced relative
///     clause, and <c>Except</c>/<c>Where</c> canonicalize sentence-final.
/// </summary>
internal abstract class ProjectAdjective
{
    /// <summary>Where this adjective's fragment lands during project-subject assembly (GRAMMAR §6).</summary>
    internal abstract AdjectivePlacement Placement { get; }

    /// <summary>
    ///     The rendered fragment, including its leading separator for inline/subject-final placements
    ///     (e.g. <c> matching `Zphil.*`</c>, <c>, except project `X`</c>); for
    ///     <see cref="AdjectivePlacement.Head" /> this is the whole phrase that replaces the head.
    /// </summary>
    internal abstract string Fragment { get; }

    /// <summary>
    ///     Whether the fragment opens a parenthetical it does not close — a project <c>Except</c> clause —
    ///     so the composer closes it with a comma at whatever junction follows (GRAMMAR §6). Meaningful only
    ///     for a <see cref="AdjectivePlacement.SubjectFinal" /> placement, the one that can end a phrase.
    /// </summary>
    internal virtual bool OpensParenthetical => false;
}
