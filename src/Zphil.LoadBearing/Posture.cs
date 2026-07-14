namespace Zphil.LoadBearing;

/// <summary>
///     A rule's lifecycle posture (DESIGN.md §5). A scope's <c>Freeze</c> is authored on the
///     surface but desugars into ordinary posture-bearing rule nodes carrying
///     <see cref="Freeze" /> (GRAMMAR §7), so checker, renderer, and baseline all walk one model.
/// </summary>
public enum Posture
{
    /// <summary>The law: violation is red. Rendered context speaks in "must".</summary>
    Enforce,

    /// <summary>Ratcheted tech debt: a descriptive current state plus a prescriptive target.</summary>
    Migrate,

    /// <summary>Here be dragons: an unenforceable interior with an enforceable boundary.</summary>
    Freeze
}