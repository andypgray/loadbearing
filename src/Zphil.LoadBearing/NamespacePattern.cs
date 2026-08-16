using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing;

/// <summary>A dot-segment-aware, case-sensitive namespace matcher (GRAMMAR §4.2).</summary>
/// <remarks>
///     Deliberately not <c>Microsoft.Extensions.FileSystemGlobbing</c>, which is path-segment based
///     and stays for file paths. Trailing <c>.*</c> is the self-inclusive subtree operator; an
///     interior standalone <c>*</c> matches exactly one segment; a partial-segment <c>*</c> matches
///     within a segment and never crosses a dot; a lone <c>*</c> matches everything.
/// </remarks>
public sealed class NamespacePattern
{
    /// <summary>The subtree operator's spelling (GRAMMAR §4.2), so the literal and its length live in one place.</summary>
    private const string SubtreeSuffix = ".*";

    private readonly bool _matchesEverything;

    private readonly string[]? _patternSegments;

    private readonly string? _subtreePrefix;

    private readonly string? _subtreePrefixDot;

    /// <summary>Creates a matcher for the given namespace glob.</summary>
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

    /// <summary>Validates a namespace glob at spec-build time (GRAMMAR §8 items 15–16).</summary>
    /// <param name="pattern">The namespace glob to check.</param>
    /// <returns>A human reason when the glob is unusable, or <c>null</c> when it is well-formed.</returns>
    /// <remarks>
    ///     Two failure modes — a blank/whitespace glob, and a <em>dead subtree pattern</em>: a trailing
    ///     <c>.*</c> whose literal prefix carries a <c>*</c>. The subtree operator compares that prefix
    ///     literally (see <see cref="Matches" />), so <c>MyApp.*.Controllers.*</c> can never match; the
    ///     reason steers the author to anchor the subtree on a literal prefix. An interior standalone
    ///     <c>*</c> with no trailing subtree operator (<c>MyApp.*.Orders</c>) is legitimate segment
    ///     matching (§4.2), and a lone <c>*</c> matches everything — both return <c>null</c>. Reason
    ///     knowledge lives here, not in the validator, so the matcher and its build-time gate cannot
    ///     drift apart.
    /// </remarks>
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

    /// <summary>Whether the given namespace matches the pattern.</summary>
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
