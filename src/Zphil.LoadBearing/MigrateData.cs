namespace Zphil.LoadBearing;

/// <summary>
///     The Migrate-specific payload of an <see cref="ArchRule" />: the descriptive
///     current state, the prescriptive target sentence, the grandfather baseline, and the
///     boy-scout policy.
/// </summary>
public sealed class MigrateData
{
    internal MigrateData(string from, string toSentence, string baselinePath, MigrationPolicy policy)
    {
        From = from;
        ToSentence = toSentence;
        BaselinePath = baselinePath;
        Policy = policy;
    }

    /// <summary>The descriptive prose of the OLD pattern (the <c>from</c> argument).</summary>
    public string From { get; }

    /// <summary>The rendered law sentence of the target constraint (the <c>to</c> argument).</summary>
    public string ToSentence { get; }

    /// <summary>
    ///     The baseline store path — never null post-build. When <c>.Baseline(path)</c> is omitted the
    ///     model is filled with the conventional default derived from the rule ID (GRAMMAR §4.4). Stored
    ///     forward-slash; the CLI resolves it against the solution directory.
    /// </summary>
    public string BaselinePath { get; }

    /// <summary>The boy-scout policy (defaults to <see cref="MigrationPolicy.MigrateIfSmall" />).</summary>
    public MigrationPolicy Policy { get; }
}