namespace Zphil.LoadBearing;

/// <summary>
///     How a rule is enforced: whether a violation fails the check, is tolerated as grandfathered
///     debt, or only warns. A rule's posture is the verb it was declared with; a scope's rules carry
///     <see cref="Quarantine" /> or <see cref="Caution" />.
/// </summary>
// A scope's Quarantine and Caution are authored on the surface but reify into ordinary posture-bearing
// rule nodes (GRAMMAR §7), so the checker, the renderers and the baseline walk one model.
public enum Posture
{
    /// <summary>Every violation fails the check. The generated context speaks in "must".</summary>
    Enforce,

    /// <summary>
    ///     Debt being paid down: violations recorded in the rule's baseline are grandfathered, and any
    ///     other violation fails the check.
    /// </summary>
    Migrate,

    /// <summary>
    ///     A scope with a boundary: a new reference into it from outside the sanctioned surface fails the
    ///     check, and a check with a diff base warns about edits inside it.
    /// </summary>
    Quarantine,

    /// <summary>
    ///     A scope with no boundary: a check with a diff base warns about edits inside it, and nothing
    ///     about it ever fails the check.
    /// </summary>
    Caution
}
