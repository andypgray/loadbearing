namespace Zphil.LoadBearing.Model;

/// <summary>
///     The constraint-position escape hatch (<c>.Must(pred, description:)</c>, GRAMMAR §5.6). The
///     predicate is stored but never evaluated in Phase 1; the required description completes
///     "must …" verbatim ("must keep type names at or under 40 characters"). A blank description
///     fails spec build (validation §8 item 5).
/// </summary>
internal sealed class MustConstraint(Selection subject, Func<ITypeInfo, bool> predicate, string description)
    : Constraint(subject)
{
    /// <summary>The stored predicate (evaluated from Phase 3).</summary>
    internal Func<ITypeInfo, bool> Predicate { get; } = predicate;

    /// <summary>The mandatory bare-infinitive description that renders in place of the lambda.</summary>
    internal string Description { get; } = description;

    internal override string VerbPhrase => "must " + Description;
}