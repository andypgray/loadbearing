namespace Zphil.LoadBearing;

/// <summary>
///     What an editor already changing a file with grandfathered violations of a <c>Migrate</c> rule
///     is asked to do about them. Set with <c>WhileYoureThere</c>; rendered into the generated agent
///     context as guidance, and nothing checks it.
/// </summary>
// MigrateIfSmall must stay the zero value: an omitted WhileYoureThere reifies as the default, and the
// rendered policy sentence reads from it (GRAMMAR §4.4).
public enum MigrationPolicy
{
    /// <summary>Migrate a grandfathered site in passing when the change is small. The default.</summary>
    MigrateIfSmall,

    /// <summary>Migrate every grandfathered site you touch.</summary>
    AlwaysMigrate,

    /// <summary>Leave grandfathered sites alone; a coordinated migration is planned.</summary>
    NeverMigrate
}
