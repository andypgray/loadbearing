namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     Line-ending normalization for comparisons against text written on another platform — the wrappers and
///     goldens write LF, the Windows shells and editors that host them write CRLF.
/// </summary>
/// <remarks>
///     Two names, deliberately. Twelve private <c>Normalize</c> copies had drifted into three different
///     semantics under one name, so which of them a comparison got was an accident of which file it lived in.
///     The name now carries the difference: a golden comparison picks trimming or not on purpose, and the
///     reader can see which it picked.
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
        return value.Replace("\r\n", "\n").Trim();
    }
}
