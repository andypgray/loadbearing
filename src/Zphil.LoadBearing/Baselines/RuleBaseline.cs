using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Baselines;

/// <summary>
///     One rule's parsed baseline section: the set of grandfathered <see cref="BaselineEntry" />s.
///     <see cref="Entries" /> is deduped and tuple-sorted (<c>((Source ?? Subject), (Target ?? ""))</c>,
///     ordinal); membership is answered in O(1) via an internal identity → entry map. Keying on the
///     Because-free <see cref="BaselineEntry" /> identity (equality excludes the attribution) means the
///     stored entry — attribution and all — is recoverable via <see cref="TryMatch" />, so the ratchet
///     can carry a grandfathered violation's original <c>because</c> into a report. A present section with
///     zero entries (captured-empty) is distinct from an absent one (uncaptured) — the absence lives in
///     <see cref="BaselineIndex" />, not here.
/// </summary>
public sealed class RuleBaseline
{
    private readonly Dictionary<BaselineEntry, BaselineEntry> _lookup;

    /// <summary>Builds a section from its entries (order and duplicates do not matter).</summary>
    public RuleBaseline(IReadOnlyCollection<BaselineEntry> entries)
    {
        Guard.NotNull(entries, nameof(entries));
        _lookup = new Dictionary<BaselineEntry, BaselineEntry>();

        // First entry of a given identity wins (matching the prior HashSet dedup) — so a later duplicate
        // never overwrites an earlier one's attribution.
        foreach (BaselineEntry entry in entries)
            if (!_lookup.ContainsKey(entry))
                _lookup.Add(entry, entry);

        Entries = _lookup.Values
            .OrderBy(e => e.Source ?? e.Subject, StringComparer.Ordinal)
            .ThenBy(e => e.Target ?? string.Empty, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The grandfathered entries, deduped and tuple-sorted ordinal.</summary>
    public IReadOnlyList<BaselineEntry> Entries { get; }

    /// <summary>The number of grandfathered entries in this section.</summary>
    public int Count => Entries.Count;

    /// <summary>Whether <paramref name="entry" /> is grandfathered by this section.</summary>
    public bool Contains(BaselineEntry entry)
    {
        return _lookup.ContainsKey(Guard.NotNull(entry, nameof(entry)));
    }

    /// <summary>
    ///     Looks up the <em>stored</em> entry whose identity equals <paramref name="identity" />,
    ///     recovering its <see cref="BaselineEntry.Because" /> attribution (which identity equality
    ///     deliberately excludes). Returns false, with <paramref name="stored" /> null, for an identity
    ///     this section does not grandfather.
    /// </summary>
    public bool TryMatch(BaselineEntry identity, out BaselineEntry? stored)
    {
        return _lookup.TryGetValue(Guard.NotNull(identity, nameof(identity)), out stored);
    }
}