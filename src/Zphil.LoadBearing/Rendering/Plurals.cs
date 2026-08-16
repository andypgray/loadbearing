namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     Count inflection for rendered lines, wherever they are composed. Between them the status, graph,
///     ratchet-survey and baseline surfaces pluralize violations, warnings, reductions, additions, rules and
///     types, and the host's incomplete-model and narrowing notices pluralize projects — every one of those
///     strings is pinned output, so the English rule has one owner here rather than a copy per renderer, and
///     a refinement (an irregular noun, an <c>s</c>/<c>es</c> split, a zero case) lands once instead of being
///     hunted for across two assemblies.
/// </summary>
/// <remarks>
///     In Core rather than beside the CLI renderers because English is not a property of the host: the
///     Roslyn host's refusals count projects in the same sentence shapes the CLI counts violations in, and a
///     second copy over there is exactly the drift this owner exists to prevent. Not to be confused with
///     <c>ProseFormat.KindPlural</c>, which looks up a grammar term's plural rather than inflecting by count.
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
