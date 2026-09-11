using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Baselines;

/// <summary>
///     One rule's captured baseline: the set of <see cref="BaselineEntry" /> identities whose
///     violations are grandfathered — reported, but not failing the check. A section with zero entries
///     is still a captured baseline; a rule with no captured baseline at all is absent from its
///     <see cref="BaselineIndex" /> rather than present and empty.
/// </summary>
public sealed class RuleBaseline
{
    private readonly Dictionary<BaselineEntry, BaselineEntry> _lookup;

    /// <summary>
    ///     Builds a section from the entries read out of a baseline file. Their order does not matter, and
    ///     entries sharing an identity are collapsed into one: the first of them keeps its reason and its
    ///     site count.
    /// </summary>
    internal RuleBaseline(IReadOnlyCollection<BaselineEntry> entries)
    {
        Guard.NotNull(entries, nameof(entries));
        _lookup = new Dictionary<BaselineEntry, BaselineEntry>();

        // First entry of a given identity wins, so a later duplicate never overwrites an earlier one's
        // attribution.
        foreach (BaselineEntry entry in entries)
            if (!_lookup.ContainsKey(entry))
                _lookup.Add(entry, entry);

        Entries = BaselineEntry.InCanonicalOrder(_lookup.Values);
    }

    /// <summary>
    ///     Gets the grandfathered entries in the baseline file's own order — by source or subject symbol
    ///     ID, then by target, ordinal — with entries sharing an identity collapsed into one.
    /// </summary>
    public IReadOnlyList<BaselineEntry> Entries { get; }

    /// <summary>Gets how many entries this section grandfathers.</summary>
    public int Count => Entries.Count;

    /// <summary>
    ///     Looks up the stored entry whose identity matches <paramref name="identity" />, returning false
    ///     with <paramref name="stored" /> null when this section grandfathers no such entry. Pass the
    ///     entry <c>Violation.BaselineIdentity()</c> returns: an identity ignores the reason and the site
    ///     count, so what comes back is the entry as the file stored it, carrying the
    ///     <see cref="BaselineEntry.Because" /> recorded with it and the
    ///     <see cref="BaselineEntry.SiteCount" /> it grandfathers.
    /// </summary>
    public bool TryMatch(BaselineEntry identity, out BaselineEntry? stored)
    {
        return _lookup.TryGetValue(Guard.NotNull(identity, nameof(identity)), out stored);
    }
}
