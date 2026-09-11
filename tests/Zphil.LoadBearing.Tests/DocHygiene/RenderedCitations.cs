using System.Text.RegularExpressions;
using Zphil.LoadBearing.Rendering;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     The citation scanner the link gate runs on. A rule that carries a <c>Citation</c> renders its page
///     as an autolinked sentence inside the rule bullet of every managed block that block appears in, and
///     this lifts each of them out of one committed context file: the rule id, the page, and the line the
///     bullet sits on. All of the logic lives here so the gate and the unit tests exercise the same code
///     path.
/// </summary>
/// <remarks>
///     <para>
///         <b>Committed bytes only.</b> This reads the generated files git tracks, never a spec: the four
///         example specs are not reachable in this process, and the self-spec's rendered block is proved
///         equal to its model by the dogfood tests while the examples' blocks are proved by CI re-rendering
///         them and failing on any diff. Reading the render also reaches every spec through one scanner,
///         and keeps the gate free of a workspace load.
///     </para>
///     <para>
///         <b>The bullet is the unit, not the line.</b> An <c>Enforce</c> rule renders the citation last;
///         a <c>Migrate</c> rule renders it between the reason and the boy-scout policy, so the autolink is
///         matched anywhere inside the bullet text rather than anchored at its end.
///     </para>
///     <para>
///         <b>The caller vouches for the text.</b> <see cref="ManagedBlock.ExtractBody" /> throws on a
///         doubled marker pair, which is what a README quoting a whole block inside a fence looks like, so
///         a caller sweeping arbitrary docs filters to the generated file name before calling in.
///     </para>
/// </remarks>
internal static class RenderedCitations
{
    private const char EmDash = (char)0x2014;

    /// <summary>A rendered rule bullet inside a managed block: <c>- `id` — text</c>.</summary>
    private static readonly Regex BulletLine =
        new($@"^- `(?<id>[^`]+)` {EmDash} (?<text>.+)$", RegexOptions.CultureInvariant);

    /// <summary>
    ///     The citation sentence inside a bullet's text: a CommonMark angle-bracket autolink, which is what
    ///     keeps the sentence's own period out of the link target.
    /// </summary>
    private static readonly Regex CitationSentence =
        new(@"See <(?<url>https?://[^>\s]+)>\.", RegexOptions.CultureInvariant);

    /// <summary>
    ///     Extracts every citation rendered inside the managed block of <paramref name="docText" />. Line
    ///     numbers are 1-based positions in the file, so a failure names the exact line. A text with no
    ///     managed block yields nothing.
    /// </summary>
    /// <exception cref="MalformedManagedBlockException">
    ///     <paramref name="docText" /> carries a malformed marker state — most often two marker pairs,
    ///     which is a hand-written doc quoting a block rather than a generated one.
    /// </exception>
    public static IReadOnlyList<RenderedCitation> Extract(string doc, string docText)
    {
        string? body = ManagedBlock.ExtractBody(docText);
        if (body is null) return [];

        int firstBodyLine = BeginMarkerLine(docText) + 1;
        string[] bodyLines = body.Split('\n');
        List<RenderedCitation> citations = new();

        for (var index = 0; index < bodyLines.Length; index++)
        {
            Match bullet = BulletLine.Match(bodyLines[index]);
            if (!bullet.Success) continue;

            string text = bullet.Groups["text"]
                .Value;
            Match citation = CitationSentence.Match(text);
            if (!citation.Success) continue;

            citations.Add(new RenderedCitation(
                doc,
                firstBodyLine + index,
                bullet.Groups["id"]
                    .Value,
                citation.Groups["url"]
                    .Value));
        }

        return citations;
    }

    // The 1-based file line the begin marker sits on, matched by trimmed exact text the way
    // ManagedBlock locates it — so the trailing carriage return of a CRLF checkout does not miss.
    // ExtractBody has already proved exactly one marker pair exists, so the walk always finds it.
    private static int BeginMarkerLine(string docText)
    {
        string[] lines = docText.Split('\n');
        int index = Array.FindIndex(lines, static line => line.Trim() == ManagedBlock.BeginMarker);

        return index + 1;
    }
}
