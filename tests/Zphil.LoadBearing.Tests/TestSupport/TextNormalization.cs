namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     Line-ending normalization for comparisons against text written on another platform — the wrappers and
///     goldens write LF, the Windows shells and editors that host them write CRLF.
/// </summary>
/// <remarks>
///     Two names, deliberately: under one name, which semantics a comparison gets is an accident of whichever
///     helper is in scope. The name carries the difference instead, so a golden comparison picks trimming or
///     not on purpose, and the reader can see which it picked.
/// </remarks>
internal static class TextNormalization
{
    /// <summary>CRLF folded to LF, leading and trailing whitespace left alone.</summary>
    internal static string NormalizedLines(this string value)
    {
        return value.Replace("\r\n", "\n");
    }

    /// <summary>CRLF folded to LF, then trimmed — for comparisons a trailing newline must not decide.</summary>
    internal static string NormalizedTrimmed(this string value)
    {
        return value.Replace("\r\n", "\n")
            .Trim();
    }

    /// <summary>
    ///     How many times <paramref name="needle" /> occurs in <paramref name="haystack" />, ordinally and
    ///     without overlaps — for the gates that count a marker rather than assert one is present.
    /// </summary>
    internal static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        for (int index = haystack.IndexOf(needle, StringComparison.Ordinal);
             index >= 0;
             index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal))
            count++;

        return count;
    }
}
