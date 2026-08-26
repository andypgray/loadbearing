using Zphil.LoadBearing.Fluent;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     The project constraint-position escape hatch (<c>.Must(pred, description:)</c>, GRAMMAR §5.6,
///     §4.10). The predicate is stored but never evaluated at spec build; the required description
///     completes "must …" verbatim ("must declare a package description"). A blank description fails spec
///     build (validation §8 item 5).
/// </summary>
/// <remarks>
///     Named with the <c>Project</c> prefix where the four packaging verbs above are bare, because
///     <see cref="MustConstraint" /> is the type-side escape hatch and two nodes cannot share a name. The
///     verb the author writes is <c>Must</c> on either stratum, and the self-spec's verb ledger strips the
///     prefix exactly as it strips <c>Member</c>.
/// </remarks>
internal sealed class ProjectMustConstraint(ProjectSelection subject, Func<IProjectInfo, bool> predicate, string description)
    : ProjectConstraint(subject)
{
    /// <summary>The stored predicate (evaluated at check time).</summary>
    internal Func<IProjectInfo, bool> Predicate { get; } = predicate;

    /// <summary>The mandatory bare-infinitive description that renders in place of the lambda.</summary>
    internal string Description { get; } = description;

    internal override string VerbPhrase => "must " + Description;
}
