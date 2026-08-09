namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     Count inflection for the CLI's rendered lines. Between them the status, graph, ratchet-survey and
///     baseline surfaces pluralize violations, warnings, reductions, additions, rules and types, and every
///     one of those strings is pinned stdout — so the English rule has one owner here rather than a copy
///     per renderer, and a refinement (an irregular noun, an <c>s</c>/<c>es</c> split, a zero case) lands
///     once instead of being hunted for four times.
/// </summary>
/// <remarks>
///     Not to be confused with Core's <c>ProseFormat.KindPlural</c>, which looks up a grammar term's
///     plural rather than inflecting by count — and which the CLI cannot reach in any case.
/// </remarks>
internal static class Plurals
{
    /// <summary>
    ///     <paramref name="noun" /> inflected for <paramref name="count" />: bare for exactly one,
    ///     suffixed with <c>s</c> for anything else — zero included.
    /// </summary>
    internal static string Noun(int count, string noun)
    {
        return count == 1 ? noun : noun + "s";
    }

    /// <summary>
    ///     The copula agreeing with <paramref name="count" />: <c>is</c> for exactly one, <c>are</c>
    ///     otherwise. Here rather than beside its one caller because it is the same rule as
    ///     <see cref="Noun" /> applied to the verb, and a sentence that counts usually needs both.
    /// </summary>
    internal static string Verb(int count)
    {
        return count == 1 ? "is" : "are";
    }
}
