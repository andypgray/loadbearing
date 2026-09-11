namespace Zphil.LoadBearing.Hosting;

/// <summary>
///     What a <c>Migrate</c> rule carries beyond an ordinary <see cref="ArchRule" />: the description of
///     the pattern the code still follows, the sentence it is moving toward, the file recording its
///     grandfathered violations, and what an editor passing through should do about them.
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

    /// <summary>
    ///     Gets the description of the pattern the code still follows, in the spec's own words — the
    ///     <c>from</c> the migration was declared with.
    /// </summary>
    public string From { get; }

    /// <summary>
    ///     Gets the target constraint as one English sentence: what the code is moving toward, and what a
    ///     violation outside the baseline is judged against.
    /// </summary>
    public string ToSentence { get; }

    /// <summary>
    ///     Gets the file recording the rule's grandfathered violations. Never null: where the spec named no
    ///     baseline it is <c>arch/baselines/{rule-id}.json</c>, each <c>/</c> in the rule ID becoming a
    ///     directory. Stored with forward slashes and resolved against the solution directory.
    /// </summary>
    public string BaselinePath { get; }

    /// <summary>
    ///     Gets what an editor already changing a file with grandfathered violations is asked to do about
    ///     them; <see cref="MigrationPolicy.MigrateIfSmall" /> where the spec set none. Rendered into the
    ///     generated agent context as guidance, and nothing checks it.
    /// </summary>
    public MigrationPolicy Policy { get; }
}
