using Zphil.LoadBearing.Baselines;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     Composes a baseline file's text the way the product composes it, for the rows that use the composer
///     as their oracle: what a valve wrote is compared against the canonical composition of exactly the
///     entries it should have written.
/// </summary>
/// <remarks>
///     The ordinal-comparer dictionary <see cref="BaselineFormat.ComposeFile" /> takes is assembly detail,
///     not something a row is asserting — spelling it out at every call site put the composer's own input
///     shape into six test files, where a change to it would have to be chased through all of them.
/// </remarks>
internal static class BaselineComposer
{
    /// <summary>The composed file for <paramref name="sections" />, one rule section each, in order.</summary>
    internal static string Compose(params (string RuleId, BaselineEntry[] Entries)[] sections)
    {
        return BaselineFormat.ComposeFile(Input(sections));
    }

    /// <summary>The composed file for one rule's <paramref name="entries" /> — the single-section case.</summary>
    internal static string Compose(string ruleId, params BaselineEntry[] entries)
    {
        return Compose((ruleId, entries));
    }

    /// <summary>
    ///     The same file as a <em>legacy</em> baseline: schemaVersion 1 and a digest computed in v1's own
    ///     frozen grammar. Composed and then downgraded rather than hand-written, because an uncounted
    ///     entry's JSON is byte-identical under both versions — only the envelope differs — so this mints
    ///     exactly what a write from before the measure existed left on disk.
    /// </summary>
    internal static string ComposeLegacy(params (string RuleId, BaselineEntry[] Entries)[] sections)
    {
        IReadOnlyDictionary<string, IReadOnlyCollection<BaselineEntry>> input = Input(sections);
        return BaselineFormat.ComposeFile(input)
            .Replace(
                $"\"schemaVersion\": {BaselineFormat.SchemaVersion}",
                $"\"schemaVersion\": {BaselineFormat.LegacySchemaVersion}")
            .Replace(
                BaselineFormat.ComputeDigest(input),
                BaselineFormat.ComputeDigest(input, BaselineFormat.LegacySchemaVersion));
    }

    /// <summary>The legacy composed file for one rule's <paramref name="entries" /> — one section.</summary>
    internal static string ComposeLegacy(string ruleId, params BaselineEntry[] entries)
    {
        return ComposeLegacy((ruleId, entries));
    }

    private static IReadOnlyDictionary<string, IReadOnlyCollection<BaselineEntry>> Input(
        (string RuleId, BaselineEntry[] Entries)[] sections)
    {
        var rules = new Dictionary<string, IReadOnlyCollection<BaselineEntry>>(StringComparer.Ordinal);
        foreach ((string ruleId, BaselineEntry[] entries) in sections) rules[ruleId] = entries;
        return rules;
    }
}
