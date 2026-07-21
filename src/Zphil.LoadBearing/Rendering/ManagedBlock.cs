using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     The dependabot-style managed block: a marker-delimited region LoadBearing owns inside an
///     <c>AGENTS.md</c> file, everything outside preserved byte-for-byte. A pure
///     string function — <c>existing text × LF-internal body → new text</c> — so it is
///     netstandard2.0-safe and deterministic; the CLI file adapter layers on BOM/bytes handling and
///     wrote/unchanged reporting.
/// </summary>
/// <remarks>
///     Markers are matched as whole lines by trimmed exact text. There is exactly one managed block
///     per file; malformed marker states throw <see cref="MalformedManagedBlockException" /> and the
///     splice is abandoned with no write. Line endings: the block body composes LF-internally always;
///     on splice into an existing file the written separator matches the file's <em>dominant</em>
///     ending (majority of <c>\n</c> preceded by <c>\r</c> ⇒ CRLF; ties / none / new file ⇒ LF).
/// </remarks>
public static class ManagedBlock
{
    /// <summary>The begin marker line — fixed text, no attributes or versions (idempotence).</summary>
    public const string BeginMarker = "<!-- loadbearing:begin -->";

    /// <summary>The end marker line — fixed text.</summary>
    public const string EndMarker = "<!-- loadbearing:end -->";

    private const string Lf = "\n";
    private const string Crlf = "\r\n";

    /// <summary>
    ///     Splices <paramref name="body" /> (composed LF-internally, no surrounding newlines) into
    ///     <paramref name="existing" />, returning the whole new file text. A null or whitespace-only
    ///     <paramref name="existing" /> counts as absent: the result is the block plus a single
    ///     trailing newline, LF. With no markers, the block is appended after the preserved content
    ///     and exactly one blank-line separator. With one marker pair, only the text strictly between
    ///     the markers is replaced; everything else — marker lines included — is preserved verbatim.
    /// </summary>
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
    ///     Returns the LF-normalized body strictly between the single marker pair, or null when the
    ///     file has no markers. Throws <see cref="MalformedManagedBlockException" /> on any malformed
    ///     marker state — so a successful non-null return also proves exactly one marker pair exists.
    /// </summary>
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
        var lines = SplitLines(text);

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