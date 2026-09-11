namespace Zphil.LoadBearing.Fluent;

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

    /// <summary>
    ///     The canonical page the rationale rests on — optional; an absolute <c>http</c>/<c>https</c> URL.
    ///     Renders as "See &lt;url&gt;." between the reason and the boy-scout policy in the agent block, as
    ///     <c>explain</c>'s and <c>check</c>'s <c>citation:</c> line, and as SARIF <c>helpUri</c> plus
    ///     <c>help.markdown</c>.
    /// </summary>
    IMigrateRule Citation(string uri);

    /// <summary>The ratcheted grandfather store path (defaults conventionally when omitted).</summary>
    IMigrateRule Baseline(string path);

    /// <summary>The boy-scout policy (defaults to <see cref="MigrationPolicy.MigrateIfSmall" />).</summary>
    IMigrateRule WhileYoureThere(MigrationPolicy policy);
}
