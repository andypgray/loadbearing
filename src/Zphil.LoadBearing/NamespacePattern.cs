using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing;

/// <summary>
///     A dot-segment-aware, case-sensitive namespace matcher (GRAMMAR §4.2) — deliberately not
///     <c>Microsoft.Extensions.FileSystemGlobbing</c>, which is path-segment based and stays for
///     file paths. Trailing <c>.*</c> is the self-inclusive subtree operator; an interior
///     standalone <c>*</c> matches exactly one segment; a partial-segment <c>*</c> matches within a
///     segment and never crosses a dot; a lone <c>*</c> matches everything.
/// </summary>
public sealed class NamespacePattern
{
    private readonly string _pattern;

    /// <summary>Creates a matcher for the given namespace glob.</summary>
    public NamespacePattern(string pattern)
    {
        _pattern = Guard.NotNullOrWhiteSpace(pattern, nameof(pattern));
    }

    /// <summary>
    ///     Validates a namespace glob at spec-build time (GRAMMAR §8 items 15–16): returns a human
    ///     reason when the glob is unusable, or <c>null</c> when it is well-formed. Two failure modes —
    ///     a blank/whitespace glob, and a <em>dead subtree pattern</em>: a trailing <c>.*</c> whose
    ///     literal prefix carries a <c>*</c>. The subtree operator compares that prefix literally (see
    ///     <see cref="Matches" />), so <c>MyApp.*.Controllers.*</c> can never match; the reason steers
    ///     the author to anchor the subtree on a literal prefix. An interior standalone <c>*</c> with no
    ///     trailing subtree operator (<c>MyApp.*.Orders</c>) is legitimate segment matching (§4.2), and a
    ///     lone <c>*</c> matches everything — both return <c>null</c>. Reason knowledge lives here, not in
    ///     the validator, so the matcher and its build-time gate cannot drift apart.
    /// </summary>
    public static string? Validate(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return "is blank";

        // Only a trailing-`.*` subtree strands a wildcard: everything before the operator is matched
        // literally, so any `*` there is dead. An interior standalone `*` (no trailing `.*`) is segment
        // matching and is fine, as is a lone `*` (length 1, never ends with `.*`).
        if (pattern.EndsWith(".*", StringComparison.Ordinal)
            && pattern.Substring(0, pattern.Length - 2).IndexOf('*') >= 0)
            return "has a trailing `.*` subtree operator but its literal prefix contains a `*`, " +
                   "which never matches; anchor the subtree on a literal prefix";

        return null;
    }

    /// <summary>Whether the given namespace matches the pattern.</summary>
    public bool Matches(string @namespace)
    {
        Guard.NotNull(@namespace, nameof(@namespace));

        if (_pattern == "*") return true;

        if (_pattern.EndsWith(".*", StringComparison.Ordinal))
        {
            // Self-inclusive subtree: `MyApp.Domain.*` matches `MyApp.Domain` and all descendants.
            string prefix = _pattern.Substring(0, _pattern.Length - 2);
            return string.Equals(@namespace, prefix, StringComparison.Ordinal)
                   || @namespace.StartsWith(prefix + ".", StringComparison.Ordinal);
        }

        string[] patternSegments = _pattern.Split('.');
        string[] namespaceSegments = @namespace.Split('.');
        if (patternSegments.Length != namespaceSegments.Length) return false;

        for (var i = 0; i < patternSegments.Length; i++)
            if (!SegmentMatches(patternSegments[i], namespaceSegments[i]))
                return false;

        return true;
    }

    private static bool SegmentMatches(string pattern, string segment)
    {
        if (pattern == "*") return segment.Length > 0;

        return pattern.IndexOf('*') < 0
            ? string.Equals(pattern, segment, StringComparison.Ordinal)
            : WildcardMatch(pattern, segment);
    }

    // Iterative within-segment wildcard match: '*' matches any run (including empty), case-sensitive,
    // never crossing a dot (segments are split before this is called).
    private static bool WildcardMatch(string pattern, string text)
    {
        var p = 0;
        var t = 0;
        int star = -1;
        var mark = 0;

        while (t < text.Length)
            if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                mark = t;
            }
            else if (p < pattern.Length && pattern[p] == text[t])
            {
                p++;
                t++;
            }
            else if (star >= 0)
            {
                p = star + 1;
                t = ++mark;
            }
            else
            {
                return false;
            }

        while (p < pattern.Length && pattern[p] == '*') p++;

        return p == pattern.Length;
    }
}