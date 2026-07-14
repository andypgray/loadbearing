namespace Zphil.LoadBearing;

/// <summary>
///     The boy-scout policy for a <see cref="Posture.Migrate" /> rule: what to do when you are
///     already editing a baselined file (GRAMMAR §5.4). <see cref="MigrateIfSmall" /> is the
///     default when <c>.WhileYoureThere(...)</c> is omitted (GRAMMAR §4.4), and it must be the
///     zero value so the reified default is deterministic.
/// </summary>
public enum MigrationPolicy
{
    /// <summary>Migrate a baselined site if the change is small (default).</summary>
    MigrateIfSmall,

    /// <summary>Always migrate a baselined site you touch.</summary>
    AlwaysMigrate,

    /// <summary>Never grow the debt, but do not require migrating what you touch.</summary>
    NeverExpand
}