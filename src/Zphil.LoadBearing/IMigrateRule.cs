namespace Zphil.LoadBearing;

/// <summary>
///     Trailers and options available on a <c>Migrate</c> rule (GRAMMAR §3.2). <c>Because</c> is
///     required (§8 item 3); <c>Baseline</c> and <c>WhileYoureThere</c> default per GRAMMAR §4.4.
/// </summary>
public interface IMigrateRule
{
    /// <summary>The rationale — required.</summary>
    IMigrateRule Because(string because);

    /// <summary>The remediation hint — optional.</summary>
    IMigrateRule Fix(string fix);

    /// <summary>The ratcheted grandfather store path (defaults conventionally when omitted).</summary>
    IMigrateRule Baseline(string path);

    /// <summary>The boy-scout policy (defaults to <see cref="MigrationPolicy.MigrateIfSmall" />).</summary>
    IMigrateRule WhileYoureThere(MigrationPolicy policy);
}