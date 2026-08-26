namespace Zphil.LoadBearing.Model;

/// <summary>
///     The project selector-position escape hatch (<c>.Where(pred, description:)</c>, GRAMMAR §5.6,
///     §4.10). The predicate is stored but never evaluated at spec build; the description renders verbatim
///     as a sentence-final relative clause continuing the noun phrase ("… whose name ends in a digit").
/// </summary>
internal sealed class ProjectWhereAdjective(Func<IProjectInfo, bool> predicate, string description) : ProjectAdjective
{
    /// <summary>The stored predicate (evaluated at check time).</summary>
    internal Func<IProjectInfo, bool> Predicate { get; } = predicate;

    /// <summary>The mandatory description that renders in place of the opaque lambda.</summary>
    internal string Description { get; } = description;

    internal override AdjectivePlacement Placement => AdjectivePlacement.SubjectFinal;

    internal override string Fragment => " " + Description;
}
