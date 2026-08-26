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
///     that family's sites. <see cref="OrderedPairs{TFirst,TSecond,TValue,TOut}" /> and
///     <see cref="OrderedRegistrations{TOut}" /> hold the sorts that make a serialized fragment and a merged
///     model byte-stable however a dictionary happened to lay its entries out — one sort for both ends of the
///     pipeline, with each end naming the comparer its own key element is ordered by.
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
    ///     Materializes a table keyed by an endpoint pair as a list ordered by the key's first element then its
    ///     second, each through the comparer its element type is ordered by, projecting each entry through
    ///     <paramref name="make" />. The key's tuple element names are erased at runtime, so every axis —
    ///     <c>(src, target)</c>, <c>(src, caught)</c>, <c>(src, member SymbolId)</c> — is the one shape here,
    ///     whether an endpoint is a name (extraction, one compilation's view) or a node key (the merge, where
    ///     one name can denote two).
    /// </summary>
    internal static List<TOut> OrderedPairs<TFirst, TSecond, TValue, TOut>(
        Dictionary<(TFirst, TSecond), TValue> map,
        IComparer<TFirst> firstComparer,
        IComparer<TSecond> secondComparer,
        Func<TFirst, TSecond, TValue, TOut> make)
    {
        return map
            .OrderBy(kv => kv.Key.Item1, firstComparer)
            .ThenBy(kv => kv.Key.Item2, secondComparer)
            .Select(kv => make(kv.Key.Item1, kv.Key.Item2, kv.Value))
            .ToList();
    }

    /// <summary>
    ///     The same over a name-keyed pair, the shape extraction's own tables take — ordinal on both elements,
    ///     so an axis there names no comparer and the sort is still spelled exactly once.
    /// </summary>
    internal static List<TOut> OrderedPairs<TValue, TOut>(
        Dictionary<(string, string), TValue> map, Func<string, string, TValue, TOut> make)
    {
        return OrderedPairs(map, StringComparer.Ordinal, StringComparer.Ordinal, make);
    }

    /// <summary>
    ///     The registration axis's own ordering, for the one table whose key is a triple rather than an
    ///     endpoint pair: lifetime first, then ordinal by service, then ordinal by implementation with the
    ///     no-implementation form sorting as the empty string.
    /// </summary>
    /// <remarks>
    ///     It sits beside <see cref="OrderedPairs{TFirst,TSecond,TValue,TOut}" /> for the same reason that one exists: both
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
    ///     A project's declared packages materialized the way the model reads them: one entry per name,
    ///     ordinal by name, projected through <paramref name="make" />. A name declared more than once keeps
    ///     its ordinally-first site, so the list is stable whatever order the declarations arrived in.
    /// </summary>
    /// <remarks>
    ///     It sits here for the same reason <see cref="OrderedRegistrations{TOut}" /> does, and folds rather
    ///     than merely sorts because both ends of the pipeline meet the duplicates too: the evaluation reads
    ///     one project's declarations, the merge reads one project's fragments back together, and a package
    ///     declared once per framework is declared once. Each end names its own output type, which is the only
    ///     part of the table that differs between them.
    /// </remarks>
    internal static List<TOut> OrderedPackages<TOut>(
        IEnumerable<FragmentPackageReference> declarations, Func<string, FragmentSite, TOut> make)
    {
        var byName = new Dictionary<string, FragmentSite>(StringComparer.Ordinal);
        foreach (FragmentPackageReference declaration in declarations)
            if (!byName.TryGetValue(declaration.Name, out FragmentSite existing)
                || declaration.Site.CompareTo(existing) < 0)
                byName[declaration.Name] = declaration.Site;

        return byName
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => make(entry.Key, entry.Value))
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
        foreach (FragmentSite site in sites) locations[index++] = Location(site);

        return locations;
    }

    /// <summary>
    ///     One site as the model's <see cref="SourceLocation" /> — the single crossing between the two
    ///     shapes, which every projection here goes through.
    /// </summary>
    internal static SourceLocation Location(FragmentSite site)
    {
        return new SourceLocation(site.File, site.Line);
    }

    /// <summary>
    ///     One site as the model's <see cref="SourceLocation" />, or <see langword="null" /> for a site that
    ///     was never recorded — the singular twin of <see cref="Locations" />, for the facts that have one
    ///     declaration rather than a set of mentions.
    /// </summary>
    internal static SourceLocation? Location(FragmentSite? site)
    {
        return site is { } known ? Location(known) : null;
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
