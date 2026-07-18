namespace Zphil.LoadBearing.Model;

/// <summary>
///     The member constraint-position escape hatch (<c>.Methods.Must(pred, description:)</c>, GRAMMAR
///     §5.6, §5.7). The predicate is stored but never evaluated in this phase; the required
///     description completes "must …" verbatim. A blank description fails spec build (validation §8
///     item 5).
/// </summary>
internal sealed class MemberMustConstraint(MemberSelection subject, Func<IMemberInfo, bool> predicate, string description)
    : MemberConstraint(subject)
{
    /// <summary>The stored predicate (evaluated from the checker phase).</summary>
    internal Func<IMemberInfo, bool> Predicate { get; } = predicate;

    /// <summary>The mandatory bare-infinitive description that renders in place of the lambda.</summary>
    internal string Description { get; } = description;

    internal override string VerbPhrase => "must " + Description;
}