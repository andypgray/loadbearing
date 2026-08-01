namespace Zphil.LoadBearing.Model;

/// <summary>
///     Where an adjective's fragment lands during subject assembly (GRAMMAR §6): as the subject
///     head (<c>OfKind</c> substitutes the plural), in front of it (<c>Authored</c> premodifies),
///     inline as a reduced relative clause, or canonicalized to sentence-final
///     (<c>Except</c>/<c>Where</c>).
/// </summary>
internal enum AdjectivePlacement
{
    /// <summary>An inline reduced relative clause, e.g. <c> in `MyApp.*`</c>.</summary>
    Inline,

    /// <summary>Substitutes the subject head plural, e.g. "interfaces".</summary>
    Head,

    /// <summary>
    ///     Premodifies the current subject head rather than replacing it, e.g. "authored types",
    ///     "authored interfaces" — so it composes with a <see cref="Head" /> substitution.
    /// </summary>
    HeadPrefix,

    /// <summary>Canonicalized to sentence-final regardless of chain position (<c>Except</c>/<c>Where</c>).</summary>
    SubjectFinal
}