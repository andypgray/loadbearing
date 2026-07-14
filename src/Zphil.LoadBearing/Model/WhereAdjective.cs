namespace Zphil.LoadBearing.Model;

/// <summary>
///     The selector-position escape hatch (<c>.Where(pred, description:)</c>, GRAMMAR §5.6). The
///     predicate is stored but never evaluated in Phase 1; the description renders verbatim as a
///     sentence-final relative clause continuing the noun phrase ("… whose name contains a digit").
/// </summary>
internal sealed class WhereAdjective(Func<ITypeInfo, bool> predicate, string description) : SelectionAdjective
{
    /// <summary>The stored predicate (evaluated from Phase 3).</summary>
    internal Func<ITypeInfo, bool> Predicate { get; } = predicate;

    /// <summary>The mandatory description that renders in place of the opaque lambda.</summary>
    internal string Description { get; } = description;

    internal override AdjectivePlacement Placement => AdjectivePlacement.SubjectFinal;

    internal override string Fragment => " " + Description;
}