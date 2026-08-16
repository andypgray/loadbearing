using System.Runtime.InteropServices;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn.Caching;

namespace Zphil.LoadBearing.Roslyn.Extraction;

/// <summary>
///     The operations both ends of the extraction pipeline run over site collections —
///     <see cref="FragmentExtractor" />'s per-compilation accumulator tables and
///     <see cref="FragmentMerger" />'s cross-fragment ones: get the set an axis unions into, materialize a
///     whole table in canonical order, and project a materialized site list the two ways the model reads it.
/// </summary>
/// <remarks>
///     Each operation carries a determinism contract that was otherwise restated once per edge axis, in both
///     files. <see cref="For{TKey}" /> holds the single <c>new SortedSet&lt;FragmentSite&gt;()</c>, so the
///     default comparer — which <em>is</em> the pinned (file, line) site ordering, see
///     <see cref="FragmentSite.CompareTo" /> — cannot be spelled differently on one axis and quietly reorder
///     that family's sites. <see cref="OrderedPairs{TValue,TOut}" /> and
///     <see cref="OrderedRegistrations{TOut}" /> hold the ordinal sorts that make a serialized fragment and a
///     merged model byte-stable however a dictionary happened to lay its entries out.
///     <see cref="FilePaths" /> holds the GRAMMAR §5.6 first-occurrence-order contract. Adding an edge axis
///     costs a table and a call, not a copy of any of those rules.
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
        ref SortedSet<FragmentSite>? sites = ref CollectionsMarshal.GetValueRefOrAddDefault(map, key, out bool exists);
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

    /// <summary>
    ///     The registration axis's own ordering, for the one table whose key is a triple rather than an
    ///     endpoint pair: lifetime first, then ordinal by service, then ordinal by implementation with the
    ///     no-implementation form sorting as the empty string.
    /// </summary>
    /// <remarks>
    ///     It sits beside <see cref="OrderedPairs{TValue,TOut}" /> for the same reason that one exists: both
    ///     ends of the pipeline materialize this table, and a rule spelled twice is a rule that can come to
    ///     be spelled two ways.
    /// </remarks>
    internal static List<TOut> OrderedRegistrations<TOut>(
        Dictionary<(Lifetime, string, string?), SortedSet<FragmentSite>> map,
        Func<Lifetime, string, string?, SortedSet<FragmentSite>, TOut> make)
    {
        return map
            .OrderBy(kv => kv.Key.Item1)
            .ThenBy(kv => kv.Key.Item2, StringComparer.Ordinal)
            .ThenBy(kv => kv.Key.Item3 ?? "", StringComparer.Ordinal)
            .Select(kv => make(kv.Key.Item1, kv.Key.Item2, kv.Key.Item3, kv.Value))
            .ToList();
    }

    /// <summary>
    ///     A site collection as the model's <see cref="SourceLocation" /> list — the projection every axis
    ///     applies on the way out of extraction, order preserved exactly as the collection holds it.
    /// </summary>
    internal static IReadOnlyList<SourceLocation> Locations(IReadOnlyCollection<FragmentSite> sites)
    {
        if (sites.Count == 0) return [];

        var locations = new SourceLocation[sites.Count];
        var index = 0;
        foreach (FragmentSite site in sites) locations[index++] = new SourceLocation(site.File, site.Line);

        return locations;
    }

    /// <summary>
    ///     The distinct files a site collection touches, in first-occurrence order (the GRAMMAR §5.6
    ///     <c>FilePaths</c> contract).
    /// </summary>
    /// <remarks>
    ///     Every collection reaching here is already (file, line) ordinal-ordered — it came out of a
    ///     <see cref="SortedSet{T}" />, or out of a fragment that built one — so first-occurrence order
    ///     <em>is</em> ordinal file order, and <c>Distinct</c> is what preserves it. The one-site case is the
    ///     overwhelming majority (a type or member declared in a single file) and skips the set entirely.
    /// </remarks>
    internal static IReadOnlyList<string> FilePaths(IReadOnlyCollection<FragmentSite> sites)
    {
        if (sites.Count == 0) return [];
        if (sites.Count == 1) return [sites.First().File];

        return sites
            .Select(s => s.File)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
