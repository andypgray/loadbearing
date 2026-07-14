using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing;

/// <summary>
///     A case-sensitive <c>*</c>-glob over simple type names — the matcher behind
///     <c>WithNameMatching</c> / <c>MustHaveNameMatching</c> (GRAMMAR §5.2, §5.3). Unlike
///     <see cref="NamespacePattern" /> there are no dot segments: a type's simple name is one token,
///     and <c>*</c> matches any run of characters including the empty run. A pattern with no <c>*</c>
///     is an exact (ordinal) match; a lone <c>*</c> matches every name.
/// </summary>
public sealed class TypeNamePattern
{
    private readonly string _pattern;

    /// <summary>Creates a matcher for the given type-name glob.</summary>
    public TypeNamePattern(string pattern)
    {
        _pattern = Guard.NotNullOrWhiteSpace(pattern, nameof(pattern));
    }

    /// <summary>Whether the given simple type name matches the pattern.</summary>
    public bool Matches(string name)
    {
        Guard.NotNull(name, nameof(name));
        return WildcardMatch(_pattern, name);
    }

    // Iterative glob match: '*' matches any run (including empty), case-sensitive. Shared shape with
    // NamespacePattern's within-segment matcher, but with no dot-crossing restriction — the whole
    // simple name is one token.
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