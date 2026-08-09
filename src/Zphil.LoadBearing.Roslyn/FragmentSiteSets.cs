using System.Runtime.InteropServices;
using Zphil.LoadBearing.Roslyn.Caching;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     The two operations both ends of the extraction pipeline run over their pair-keyed accumulator tables —
///     <see cref="FragmentExtractor" />'s per-compilation ones and <see cref="FragmentMerger" />'s
///     cross-fragment ones: get the set an axis unions into, and materialize a whole table in canonical order.
/// </summary>
/// <remarks>
///     Each operation carries a determinism contract that was otherwise restated once per edge axis, in both
///     files. <see cref="For{TKey}" /> holds the single <c>new SortedSet&lt;FragmentSite&gt;()</c>, so the
///     default comparer — which <em>is</em> the pinned (file, line) site ordering, see
///     <see cref="FragmentSite.CompareTo" /> — cannot be spelled differently on one axis and quietly reorder
///     that family's sites. <see cref="OrderedPairs{TValue,TOut}" /> holds the two ordinal sorts that make a
///     serialized fragment and a merged model byte-stable however a dictionary happened to lay its entries
///     out. Adding an edge axis costs a table and a call, not a copy of either rule.
/// </remarks>
internal static class FragmentSiteSets
{
    /// <summary>
    ///     The site set <paramref name="key" /> unions into, adding an empty one on first mention. One hash
    ///     lookup either way.
    /// </summary>
    internal static SortedSet<FragmentSite> For<TKey>(Dictionary<TKey, SortedSet<FragmentSite>> map, TKey key)
        where TKey : notnull
    {
        ref var sites = ref CollectionsMarshal.GetValueRefOrAddDefault(map, key, out bool exists);
        if (!exists) sites = [];
        return sites!;
    }

    /// <summary>
    ///     Materializes a table keyed by an endpoint pair as a list ordered ordinal by the key's first element
    ///     then its second, projecting each entry through <paramref name="make" />. The key's tuple element
    ///     names are erased at runtime, so every axis — <c>(src, target)</c>, <c>(src, caught)</c>,
    ///     <c>(src, member SymbolId)</c> — is the one shape here.
    /// </summary>
    internal static List<TOut> OrderedPairs<TValue, TOut>(
        Dictionary<(string, string), TValue> map, Func<string, string, TValue, TOut> make)
    {
        return map
            .OrderBy(kv => kv.Key.Item1, StringComparer.Ordinal)
            .ThenBy(kv => kv.Key.Item2, StringComparer.Ordinal)
            .Select(kv => make(kv.Key.Item1, kv.Key.Item2, kv.Value))
            .ToList();
    }
}
