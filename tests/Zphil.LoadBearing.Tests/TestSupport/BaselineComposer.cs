using Zphil.LoadBearing.Baselines;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     Composes a baseline file's text the way the product composes it, for the rows that use the composer
///     as their oracle: what a valve wrote is compared against the canonical composition of exactly the
///     entries it should have written.
/// </summary>
/// <remarks>
///     The ordinal-comparer dictionary <see cref="BaselineFormat.ComposeFile" /> takes is assembly detail,
///     not something a row is asserting — spelling it out at every call site would put the composer's own
///     input shape into every test file that composes one, where a change to it would have to be chased
///     through all of them.
/// </remarks>
internal static class BaselineComposer
{
    /// <summary>The composed file for <paramref name="sections" />, one rule section each, in order.</summary>
    internal static string Compose(params (string RuleId, BaselineEntry[] Entries)[] sections)
    {
        return BaselineFormat.ComposeFile(Rules(sections));
    }

    /// <summary>The composed file for one rule's <paramref name="entries" /> — the single-section case.</summary>
    internal static string Compose(string ruleId, params BaselineEntry[] entries)
    {
        return Compose((ruleId, entries));
    }

    /// <summary>
    ///     The same file as a <em>legacy</em> baseline: schemaVersion 1, one whole-file digest computed in
    ///     v1's own frozen grammar, and no per-entry seal. Composed and then downgraded rather than
    ///     hand-written, so this mints exactly what a write from before either measure existed left on
    ///     disk: what comes off is what the composer put on, seal fragment by seal fragment, and what goes
    ///     in its place is one envelope line the product computes.
    /// </summary>
    /// <exception cref="ArgumentException">
    ///     An entry carries a <see cref="BaselineEntry.SiteCount" />. A legacy file has no measure, so
    ///     downgrading a counted entry would mint a file no write ever produced — and one the reader
    ///     refuses as malformed rather than reading as legacy.
    /// </exception>
    internal static string ComposeLegacy(params (string RuleId, BaselineEntry[] Entries)[] sections)
    {
        IReadOnlyDictionary<string, IReadOnlyCollection<BaselineEntry>> input = Rules(sections);
        bool anyCounted = sections
            .SelectMany(section => section.Entries)
            .Any(entry => entry.SiteCount is not null);
        if (anyCounted)
            throw new ArgumentException(
                "A legacy baseline carries no site count — compose the entries without one.", nameof(sections));

        string composed = BaselineFormat.ComposeFile(input);
        foreach ((string ruleId, BaselineEntry[] entries) in sections)
        foreach (BaselineEntry entry in entries)
        {
            string seal = BaselineFormat.ComputeSeal(ruleId, entry);
            composed = composed.Replace($", \"seal\": \"{seal}\"", string.Empty);
        }

        string legacyDigest = BaselineFormat.LegacyDigest(input);
        return composed.Replace(
            $"  \"schemaVersion\": {BaselineFormat.SchemaVersion},\n",
            $"  \"schemaVersion\": {BaselineFormat.LegacySchemaVersion},\n"
            + $"  \"digest\": \"{legacyDigest}\",\n");
    }

    /// <summary>The legacy composed file for one rule's <paramref name="entries" /> — one section.</summary>
    internal static string ComposeLegacy(string ruleId, params BaselineEntry[] entries)
    {
        return ComposeLegacy((ruleId, entries));
    }

    /// <summary>
    ///     The composer's input for <paramref name="sections" /> — one ordinal-keyed rule section each, in
    ///     order: the shape <see cref="BaselineFormat.ComposeFile" /> and the seal and digest verbs take, for
    ///     the rows that call the format directly rather than through
    ///     <see cref="Compose(ValueTuple{string, BaselineEntry[]}[])" />.
    /// </summary>
    internal static IReadOnlyDictionary<string, IReadOnlyCollection<BaselineEntry>> Rules(
        params (string RuleId, BaselineEntry[] Entries)[] sections)
    {
        var rules = new Dictionary<string, IReadOnlyCollection<BaselineEntry>>(StringComparer.Ordinal);
        foreach ((string ruleId, BaselineEntry[] entries) in sections) rules[ruleId] = entries;
        return rules;
    }
}
