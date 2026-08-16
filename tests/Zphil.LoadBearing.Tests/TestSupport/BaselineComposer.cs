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
        var rules = new Dictionary<string, IReadOnlyCollection<BaselineEntry>>(StringComparer.Ordinal);
        foreach ((string ruleId, BaselineEntry[] entries) in sections) rules[ruleId] = entries;
        return BaselineFormat.ComposeFile(rules);
    }

    /// <summary>The composed file for one rule's <paramref name="entries" /> — the single-section case.</summary>
    internal static string Compose(string ruleId, params BaselineEntry[] entries)
    {
        return Compose((ruleId, entries));
    }
}
