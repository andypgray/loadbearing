using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Baselines;

/// <summary>
///     One rule's parsed baseline section: the set of grandfathered <see cref="BaselineEntry" />s.
///     <see cref="Entries" /> is deduped and tuple-sorted (<c>((Source ?? Subject), (Target ?? ""))</c>,
///     ordinal); membership is answered in O(1) via an internal <see cref="HashSet{T}" />. A present
///     section with zero entries (captured-empty) is distinct from an absent one (uncaptured) — the
///     absence lives in <see cref="BaselineIndex" />, not here.
/// </summary>
public sealed class RuleBaseline
{
    private readonly HashSet<BaselineEntry> _lookup;

    /// <summary>Builds a section from its entries (order and duplicates do not matter).</summary>
    public RuleBaseline(IReadOnlyCollection<BaselineEntry> entries)
    {
        _lookup = new HashSet<BaselineEntry>(Guard.NotNull(entries, nameof(entries)));
        Entries = _lookup
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
        return _lookup.Contains(Guard.NotNull(entry, nameof(entry)));
    }
}