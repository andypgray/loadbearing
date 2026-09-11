using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     The marker-delimited region of a text file that LoadBearing owns, everything outside the markers
///     preserved byte for byte. Two pure string functions: <see cref="Splice" /> puts a body in and
///     <see cref="ExtractBody" /> reads one back out, so the host decides for itself whether and when to
///     write the file.
/// </summary>
/// <remarks>
///     A file carries exactly one managed block. Markers are matched as whole lines, ignoring leading
///     and trailing whitespace, so the marker text must not appear anywhere else in the file, examples
///     included. Any other marker state (a begin with no end, an end before a begin, a second of either)
///     throws <see cref="MalformedManagedBlockException" />, and nothing is spliced or extracted.
///     Compose the body with LF line endings: <see cref="Splice" /> writes whichever ending the existing
///     file mostly uses, CRLF when a strict majority of its lines end that way and LF otherwise.
/// </remarks>
// The dependabot-style managed block, kept a pure string function (existing text + LF body -> new
// text) so it stays netstandard2.0-safe and deterministic.
public static class ManagedBlock
{
    /// <summary>
    ///     The line that opens the managed block. Fixed text carrying no version and no attributes, so
    ///     re-rendering an unchanged spec leaves the file byte for byte as it was.
    /// </summary>
    public const string BeginMarker = "<!-- loadbearing:begin -->";

    /// <summary>The line that closes the managed block. Fixed text, carrying no version and no attributes.</summary>
    public const string EndMarker = "<!-- loadbearing:end -->";

    private const string Lf = "\n";
    private const string Crlf = "\r\n";

    /// <summary>
    ///     Splices <paramref name="body" /> into <paramref name="existing" /> and returns the whole new file
    ///     text; nothing is written. Compose the body with LF line endings and no surrounding blank lines.
    /// </summary>
    /// <exception cref="MalformedManagedBlockException">
    ///     <paramref name="existing" /> carries a malformed marker state; nothing is spliced.
    /// </exception>
    /// <remarks>
    ///     A null or blank <paramref name="existing" /> counts as a file that is not there: the result is
    ///     the marked block and one trailing newline, all LF. Text with no markers keeps everything it has
    ///     and the block is appended after one blank line. Text with a marker pair keeps everything except
    ///     what lies strictly between the markers, the marker lines themselves included, and the body
    ///     replaces that. The line endings written are the ones the existing text mostly uses.
    /// </remarks>
    public static string Splice(string? existing, string body)
    {
        Guard.NotNull(body, nameof(body));

        if (string.IsNullOrWhiteSpace(existing))
            return BeginMarker + Lf + body + Lf + EndMarker + Lf;

        string newline = DominantNewline(existing!);
        MarkerLocation? location = LocateMarkers(existing!);

        if (location is null)
        {
            string preserved = TrimTrailingNewlines(existing!);
            string block = BeginMarker + newline + ConvertNewlines(body, newline) + newline + EndMarker;
            return preserved + newline + newline + block + newline;
        }

        string prefix = existing!.Substring(0, location.BodyStart);
        string suffix = existing.Substring(location.BodyEnd);
        return prefix + ConvertNewlines(body, newline) + newline + suffix;
    }

    /// <summary>
    ///     The body between the markers of <paramref name="existing" />, its line endings normalized to LF
    ///     and its final newline removed, or null when the text carries no markers at all. Compare it
    ///     against a freshly composed body to tell whether a committed file is still up to date.
    /// </summary>
    /// <exception cref="MalformedManagedBlockException">
    ///     The markers are in any malformed state, so a non-null return also says that exactly one
    ///     well-formed marker pair exists.
    /// </exception>
    public static string? ExtractBody(string existing)
    {
        Guard.NotNull(existing, nameof(existing));

        MarkerLocation? location = LocateMarkers(existing);
        if (location is null) return null;

        string region = existing.Substring(location.BodyStart, location.BodyEnd - location.BodyStart);
        return StripOneTrailingNewline(region).Replace(Crlf, Lf);
    }

    // The dominant existing ending: CRLF iff a strict majority of newlines are CRLF.
    private static string DominantNewline(string text)
    {
        var total = 0;
        var crlf = 0;
        for (var i = 0; i < text.Length; i++)
            if (text[i] == '\n')
            {
                total++;
                if (i > 0 && text[i - 1] == '\r') crlf++;
            }

        return crlf * 2 > total ? Crlf : Lf;
    }

    private static string ConvertNewlines(string body, string newline)
    {
        // Body is LF-internal by contract; normalize defensively, then apply the target separator.
        string lfBody = body.Replace(Crlf, Lf);
        return newline == Lf ? lfBody : lfBody.Replace(Lf, newline);
    }

    private static string TrimTrailingNewlines(string text)
    {
        int end = text.Length;
        while (end > 0 && (text[end - 1] == '\n' || text[end - 1] == '\r')) end--;

        return text.Substring(0, end);
    }

    private static string StripOneTrailingNewline(string text)
    {
        if (text.EndsWith(Crlf)) return text.Substring(0, text.Length - 2);
        if (text.EndsWith(Lf)) return text.Substring(0, text.Length - 1);

        return text;
    }

    // Locates the single begin/end marker pair, validating that exactly one well-ordered pair exists.
    // Returns null when the file carries no markers at all (the append case).
    private static MarkerLocation? LocateMarkers(string text)
    {
        List<PhysicalLine> lines = SplitLines(text);

        var beginIndices = new List<int>();
        var endIndices = new List<int>();
        for (var i = 0; i < lines.Count; i++)
        {
            string trimmed = lines[i].Text.Trim();
            if (trimmed == BeginMarker) beginIndices.Add(i);
            else if (trimmed == EndMarker) endIndices.Add(i);
        }

        if (beginIndices.Count == 0 && endIndices.Count == 0) return null;

        if (beginIndices.Count > 1)
            throw new MalformedManagedBlockException(
                $"Malformed managed block: {beginIndices.Count} '{BeginMarker}' markers (expected exactly one). "
                + "The marker text must not appear anywhere else in the file, including in examples.");
        if (endIndices.Count > 1)
            throw new MalformedManagedBlockException(
                $"Malformed managed block: {endIndices.Count} '{EndMarker}' markers (expected exactly one). "
                + "The marker text must not appear anywhere else in the file, including in examples.");
        if (beginIndices.Count == 0)
            throw new MalformedManagedBlockException(
                $"Malformed managed block: '{EndMarker}' without a matching '{BeginMarker}'.");
        if (endIndices.Count == 0)
            throw new MalformedManagedBlockException(
                $"Malformed managed block: '{BeginMarker}' without a matching '{EndMarker}'.");
        if (beginIndices[0] > endIndices[0])
            throw new MalformedManagedBlockException(
                $"Malformed managed block: '{EndMarker}' precedes '{BeginMarker}'.");

        return new MarkerLocation(lines[beginIndices[0]].NextStart, lines[endIndices[0]].Start);
    }

    private static List<PhysicalLine> SplitLines(string text)
    {
        var lines = new List<PhysicalLine>();
        var start = 0;
        while (start <= text.Length)
        {
            int newlineIndex = text.IndexOf('\n', start);
            if (newlineIndex < 0)
            {
                if (start < text.Length) lines.Add(new PhysicalLine(text.Substring(start), start, text.Length));
                break;
            }

            lines.Add(new PhysicalLine(text.Substring(start, newlineIndex - start), start, newlineIndex + 1));
            start = newlineIndex + 1;
        }

        return lines;
    }

    // A physical line: its text without the terminating '\n' (a trailing '\r' may remain and is
    // stripped by Trim() when matching markers), the offset it starts at, and the offset of the
    // first character after its terminator.
    private readonly struct PhysicalLine(string text, int start, int nextStart)
    {
        public string Text { get; } = text;
        public int Start { get; } = start;
        public int NextStart { get; } = nextStart;
    }

    // The splice boundaries of the managed region: the offset just past the begin marker's newline
    // and the offset at the start of the end marker line. The text between is what gets replaced.
    private sealed class MarkerLocation(int bodyStart, int bodyEnd)
    {
        public int BodyStart { get; } = bodyStart;
        public int BodyEnd { get; } = bodyEnd;
    }
}
