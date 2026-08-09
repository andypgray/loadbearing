namespace Zphil.LoadBearing.Internal;

/// <summary>
///     The shared iterative <c>*</c>-glob matcher behind both simple-name and namespace-segment
///     matching (GRAMMAR §4.2, §5.2). <c>*</c> matches any run of characters including the empty run;
///     every other character is an ordinal (case-sensitive) match. It has no notion of a dot
///     separator — dot-crossing is decided by whoever tokenizes the input before calling in.
/// </summary>
internal static class Wildcard
{
    internal static bool Match(string pattern, string text)
    {
        // star/mark remember the last '*' seen and how far the text had advanced when it was taken, so a
        // dead end backtracks by letting that '*' swallow one more character instead of rescanning from
        // the start; star = -1 means no '*' is available to backtrack to, so the mismatch is final.
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
