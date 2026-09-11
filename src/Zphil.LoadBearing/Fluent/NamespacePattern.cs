using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Fluent;

/// <summary>
///     Matches namespaces against a glob, the way a spec's namespace patterns are matched. Matching is
///     by dot-separated segment and case-sensitive, and five rules cover it: a trailing <c>.*</c>
///     covers the namespace itself and everything beneath it, so <c>MyApp.Domain.*</c> matches
///     <c>MyApp.Domain</c> and <c>MyApp.Domain.Orders</c> but not <c>MyApp.DomainX</c>; a <c>*</c>
///     standing alone as a segment matches exactly one segment, so <c>MyApp.*.Orders</c> matches
///     <c>MyApp.Sales.Orders</c> but neither <c>MyApp.Orders</c> nor <c>MyApp.A.B.Orders</c>; a
///     <c>*</c> inside a segment matches within that segment and never crosses a dot, so
///     <c>MyApp.Legacy*</c> matches <c>MyApp.LegacyBilling</c> but not <c>MyApp.Legacy.Billing</c>; a
///     glob with no <c>*</c> matches that one namespace; and a lone <c>*</c> matches every namespace.
///     Namespaces only: this is not a file-path matcher.
/// </summary>
public sealed class NamespacePattern
{
    /// <summary>The subtree operator's spelling (GRAMMAR §4.2), so the literal and its length live in one place.</summary>
    private const string SubtreeSuffix = ".*";

    private readonly bool _matchesEverything;

    private readonly string[]? _patternSegments;

    private readonly string? _subtreePrefix;

    private readonly string? _subtreePrefixDot;

    /// <summary>
    ///     Creates a matcher for a namespace glob, such as <c>new NamespacePattern("MyApp.Domain.*")</c>.
    ///     Throws <see cref="ArgumentNullException" /> when the glob is null and
    ///     <see cref="ArgumentException" /> when it is blank; <see cref="Validate" /> answers the same
    ///     question without throwing, and also catches a glob that can never match.
    /// </summary>
    public NamespacePattern(string pattern)
    {
        string glob = Guard.NotNullOrWhiteSpace(pattern, nameof(pattern));

        // The pattern is immutable, so the whole of what it means is invariant: which of the three shapes
        // it is, the subtree prefix and its `prefix + "."` probe, or the literal segments. Deriving it here
        // rather than inside Matches is what makes matching allocation-free — Matches runs once per type in
        // the universe, per pattern, per rule, and the alternative is a substring or a split per candidate.
        // Those three are the whole of what matching reads, so the glob text itself needs no field.
        _matchesEverything = glob == "*";
        if (_matchesEverything) return;

        if (TryParseSubtree(glob, out string prefix))
        {
            _subtreePrefix = prefix;
            _subtreePrefixDot = prefix + ".";
            return;
        }

        _patternSegments = glob.Split('.');
    }

    /// <summary>
    ///     Checks whether a namespace glob is usable, before it is made into a matcher. Two globs are not:
    ///     a blank one, and one ending in <c>.*</c> that carries another <c>*</c> before it
    ///     (<c>MyApp.*.Controllers.*</c>) — everything before a trailing <c>.*</c> is matched literally, so
    ///     such a glob can never match anything. Every other glob is usable, an interior <c>*</c> segment
    ///     (<c>MyApp.*.Orders</c>) and a lone <c>*</c> included.
    /// </summary>
    /// <param name="pattern">The namespace glob to check.</param>
    /// <returns>A sentence saying what is wrong with the glob, or <c>null</c> when it is usable.</returns>
    // The reason text lives beside the matcher rather than in the validator, so the two cannot drift:
    // what Matches treats as literal is exactly what this refuses to strand a `*` in (GRAMMAR §4.2,
    // §8 items 15-16).
    public static string? Validate(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return "is blank";

        // Only a trailing-`.*` subtree strands a wildcard: everything before the operator is matched
        // literally, so any `*` there is dead. An interior standalone `*` (no trailing `.*`) is segment
        // matching and is fine, as is a lone `*` (length 1, never ends with `.*`).
        if (TryParseSubtree(pattern, out string prefix) && prefix.IndexOf('*') >= 0)
            return "has a trailing `.*` subtree operator but its literal prefix contains a `*`, " +
                   "which never matches; anchor the subtree on a literal prefix";

        return null;
    }

    /// <summary>
    ///     Whether a namespace matches the glob, by the segment rules the pattern carries. Pass the
    ///     namespace on its own, without a type name; the empty string is the global namespace. Throws
    ///     <see cref="ArgumentNullException" /> when it is null.
    /// </summary>
    public bool Matches(string @namespace)
    {
        Guard.NotNull(@namespace, nameof(@namespace));

        if (_matchesEverything) return true;

        // Self-inclusive subtree: `MyApp.Domain.*` matches `MyApp.Domain` and all descendants.
        if (_subtreePrefix is not null) return PrefixCovers(_subtreePrefix, _subtreePrefixDot!, @namespace);

        string[] patternSegments = _patternSegments!;
        string[] namespaceSegments = @namespace.Split('.');
        if (patternSegments.Length != namespaceSegments.Length) return false;

        for (var i = 0; i < patternSegments.Length; i++)
            if (!SegmentMatches(patternSegments[i], namespaceSegments[i]))
                return false;

        return true;
    }

    /// <summary>
    ///     Decomposes the trailing-<c>.*</c> subtree operator (GRAMMAR §4.2): true with the literal prefix
    ///     when <paramref name="glob" /> carries it, false with the glob itself when it does not. The one
    ///     home for the operator's spelling, so nothing else strips two characters by hand.
    /// </summary>
    internal static bool TryParseSubtree(string glob, out string prefix)
    {
        if (glob.EndsWith(SubtreeSuffix, StringComparison.Ordinal))
        {
            prefix = glob.Substring(0, glob.Length - SubtreeSuffix.Length);
            return true;
        }

        prefix = glob;
        return false;
    }

    /// <summary>
    ///     Whether a subtree rooted at <paramref name="prefix" /> covers <paramref name="candidate" /> —
    ///     the self-inclusive rule the operator carries: the prefix itself, or anything below a dot.
    /// </summary>
    internal static bool PrefixCovers(string prefix, string candidate)
    {
        return PrefixCovers(prefix, prefix + ".", candidate);
    }

    // The same rule with the `prefix + "."` probe supplied, for the matcher's hot path — it precomputes the
    // probe once in the constructor rather than concatenating a string per candidate type.
    private static bool PrefixCovers(string prefix, string prefixDot, string candidate)
    {
        return string.Equals(candidate, prefix, StringComparison.Ordinal)
               || candidate.StartsWith(prefixDot, StringComparison.Ordinal);
    }

    private static bool SegmentMatches(string pattern, string segment)
    {
        if (pattern == "*") return segment.Length > 0;

        return pattern.IndexOf('*') < 0
            ? string.Equals(pattern, segment, StringComparison.Ordinal)
            : Wildcard.Match(pattern, segment);
    }
}
