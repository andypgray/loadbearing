namespace Zphil.LoadBearing;

/// <summary>
///     A rule's lifecycle posture. A scope's <c>Quarantine</c> and <c>Caution</c> are authored on the
///     surface but desugar into ordinary posture-bearing rule nodes carrying
///     <see cref="Quarantine" />/<see cref="Caution" /> (GRAMMAR §7), so checker, renderer, and baseline
///     all walk one model.
/// </summary>
public enum Posture
{
    /// <summary>The law: violation is red. Rendered context speaks in "must".</summary>
    Enforce,

    /// <summary>Ratcheted tech debt: a descriptive current state plus a prescriptive target.</summary>
    Migrate,

    /// <summary>Here be dragons: an unenforceable interior with an enforceable boundary.</summary>
    Quarantine,

    /// <summary>
    ///     Here be dragons, with no boundary: an unenforceable interior nothing is kept out of. The first
    ///     posture with no red state — its one rule, the tripwire, warns and can never fail.
    /// </summary>
    Caution
}
