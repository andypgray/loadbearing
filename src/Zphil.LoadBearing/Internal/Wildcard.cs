namespace Zphil.LoadBearing.Internal;

/// <summary>
///     The shared iterative <c>*</c>-glob matcher behind both simple-name and namespace-segment
///     matching (GRAMMAR §4.2, §5.2). <c>*</c> matches any run of characters including the empty run;
///     every other character is an ordinal (case-sensitive) match. It has no notion of a dot
///     separator — dot-crossing is the caller's concern: <see cref="TypeNamePattern" /> feeds the whole
///     simple name as a single token, while <see cref="NamespacePattern" /> splits on dots and matches
///     one segment at a time, so a <c>*</c> never crosses a dot there.
/// </summary>
internal static class Wildcard
{
    internal static bool Match(string pattern, string text)
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