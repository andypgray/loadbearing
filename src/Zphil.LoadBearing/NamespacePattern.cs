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