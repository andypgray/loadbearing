using System.Text.RegularExpressions;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     The source-anchor checker the walkthrough gate runs on. It lifts every <c>file:line — message</c>
///     anchor out of a doc's fenced code blocks, derives the content token the committed source line
///     must carry, and decides whether each anchor still matches its source — by content, or by a
///     landmark pin that keys a documented walkthrough edit to the committed line it lands on. All of
///     the logic lives here so the gate and the unit tests exercise the same code path.
/// </summary>
internal static class SourceAnchors
{
    /// <summary>Which bucket resolved an anchor against its committed source, or that none did.</summary>
    public enum AnchorBucket
    {
        /// <summary>The derived token sits on the anchor's reported line.</summary>
        Content,

        /// <summary>A landmark pinned the committed line the anchor's documented edit is keyed to.</summary>
        Landmark,

        /// <summary>Neither bucket matched; the anchor no longer matches its committed source.</summary>
        Unresolved
    }

    private const char EmDash = (char)0x2014;

    private static readonly string[] ReferenceVerbs = ["references", "constructs", "injects", "exposes", "catches"];

    private static readonly Regex AnchorLine =
        new($@"^\s*(src/\S+\.cs):(\d+) {EmDash} (.+)$", RegexOptions.CultureInvariant);

    /// <summary>
    ///     Extracts every anchor from the fenced code blocks of <paramref name="docText" />. Anchors
    ///     outside fences are ignored, because inline prose short-forms restate the fenced anchors and are
    ///     commentary rather than the pin.
    /// </summary>
    public static IReadOnlyList<SourceAnchor> Extract(string doc, string docText)
    {
        List<SourceAnchor> anchors = new();

        foreach ((string text, int number) in FencedLines(docText))
        {
            Match match = AnchorLine.Match(text);
            if (!match.Success) continue;

            string file = match.Groups[1].Value;
            int sourceLine = int.Parse(match.Groups[2].Value);
            string message = match.Groups[3]
                .Value.Trim();
            anchors.Add(new SourceAnchor(doc, number, file, sourceLine, message));
        }

        return anchors;
    }

    /// <summary>
    ///     Splits <paramref name="docText" /> into the content of each fenced code block, one list per
    ///     block in document order, each line paired with its 1-based line number in the doc so a caller
    ///     can name the exact place a quote lives. The fence lines themselves are dropped and the lines
    ///     between them are kept verbatim, and an unclosed fence keeps every line to the end of the text as
    ///     its final block — <see cref="Scan" /> decides all of that.
    /// </summary>
    /// <remarks>
    ///     A caller needing both block identity and position — a <c>grandfathered: N</c> sub-line takes its
    ///     rule id from the header line above it <em>in the same fence</em>, and must still fail naming
    ///     <c>doc:line</c> — reads this directly.
    /// </remarks>
    public static IReadOnlyList<IReadOnlyList<(string Text, int Number)>> FencedBlocks(string docText)
    {
        List<IReadOnlyList<(string Text, int Number)>> blocks = new();
        List<(string Text, int Number)>? current = null;

        foreach ((string text, int number, LineKind kind) in Scan(docText))
            if (kind == LineKind.FenceOpen)
            {
                current = new List<(string, int)>();
            }
            else if (current is not null && kind == LineKind.FenceClose)
            {
                blocks.Add(current);
                current = null;
            }
            else if (current is not null && kind == LineKind.Fenced)
            {
                current.Add((text, number));
            }

        if (current is not null) blocks.Add(current);

        return blocks;
    }

    /// <summary>
    ///     Every line of <paramref name="docText" /> that sits inside a fenced code block, paired with its
    ///     1-based line number in the doc — <see cref="FencedBlocks" /> flattened, for the callers that
    ///     want positions and do not care which block a line came from.
    /// </summary>
    public static IReadOnlyList<(string Text, int Number)> FencedLines(string docText)
    {
        return FencedBlocks(docText)
            .SelectMany(static block => block)
            .ToArray();
    }

    /// <summary>
    ///     The content lines of each fenced code block of <paramref name="docText" />, one list per block
    ///     in document order — <see cref="FencedBlocks" /> without the line numbers, for the callers that
    ///     hold a quoted block to the source it was cut from and locate it by a marker rather than by
    ///     position.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> Fences(string docText)
    {
        return FencedBlocks(docText)
            .Select(static block => (IReadOnlyList<string>)block
                .Select(static line => line.Text)
                .ToArray())
            .ToArray();
    }

    /// <summary>
    ///     Every line of <paramref name="docText" /> outside every fenced code block, paired with its
    ///     1-based line number in the doc — the fence delimiter lines and everything between them dropped.
    ///     This is the doc's prose, which the prose-hygiene checks measure.
    /// </summary>
    public static IReadOnlyList<(string Text, int Number)> ProseLines(string docText)
    {
        return Scan(docText)
            .Where(static line => line.Kind == LineKind.Prose)
            .Select(static line => (line.Text, line.Number))
            .ToArray();
    }

    /// <summary>
    ///     Every line of <paramref name="docText" /> that <see cref="FencedLines" /> does not cover: the
    ///     prose <em>and</em> the fence delimiter lines themselves. This is the complement by line number,
    ///     for the sweeps that ask what a doc says where no fenced gate is watching — only the quoted
    ///     content between the delimiters is exempt from those, and an opening delimiter's info string is
    ///     text like any other line.
    /// </summary>
    public static IReadOnlyList<(string Text, int Number)> UnfencedLines(string docText)
    {
        return Scan(docText)
            .Where(static line => line.Kind != LineKind.Fenced)
            .Select(static line => (line.Text, line.Number))
            .ToArray();
    }

    /// <summary>
    ///     Derives the content token the committed source line must contain from an anchor message's
    ///     tail. A <c>uses A.B.Member</c> tail yields the last two dotted segments (so an accessor keeps
    ///     its declaring type); a <c>references</c>/<c>constructs</c>/<c>injects</c>/<c>exposes</c>/
    ///     <c>catches</c> target yields the target's last dotted segment with any generic suffix removed;
    ///     a member tail ending in <c>()</c> yields the member name; and a bare dotted type yields its
    ///     last segment.
    /// </summary>
    public static string DeriveToken(string message)
    {
        string trimmed = message.Trim();

        if (TryVerbTarget(trimmed, "uses", out string usesTarget)) return LastTwoSegments(usesTarget);

        foreach (string verb in ReferenceVerbs)
            if (TryVerbTarget(trimmed, verb, out string target))
                return LastSegment(StripGeneric(target));

        if (trimmed.EndsWith("()", StringComparison.Ordinal)) return LastSegment(trimmed[..^2]);

        return LastSegment(StripGeneric(trimmed));
    }

    /// <summary>
    ///     Decides whether <paramref name="anchor" /> still matches its committed source. It matches when
    ///     the derived token sits on the reported line (the content bucket); failing that, when a landmark
    ///     for the anchor's key pins a committed line whose content holds the landmark snippet (the
    ///     landmark bucket). An anchor satisfying neither is unresolved, with a reason naming the doc, the
    ///     source line, and what was expected. <paramref name="exampleRoot" /> is the directory the
    ///     anchor's path hangs off, or empty when the doc quotes this repository's own sources and the path
    ///     is already repository-relative. <paramref name="readLines" /> reads a repository-relative path
    ///     into its lines, or returns <see langword="null" /> when the file is absent.
    /// </summary>
    public static AnchorResult Classify(
        SourceAnchor anchor,
        string exampleRoot,
        IReadOnlyDictionary<AnchorKey, Landmark> landmarks,
        Func<string, IReadOnlyList<string>?> readLines)
    {
        string repoRelativeFile = RepoRelative(exampleRoot, anchor.File);
        IReadOnlyList<string>? lines = readLines(repoRelativeFile);
        if (lines is null) return Unresolved(anchor, $"source file not found at {repoRelativeFile}");

        string token = DeriveToken(anchor.Message);
        bool contentMatch = anchor.Line >= 1
                            && anchor.Line <= lines.Count
                            && lines[anchor.Line - 1]
                                .Contains(token, StringComparison.Ordinal);
        if (contentMatch) return new AnchorResult(AnchorBucket.Content, null);

        AnchorKey key = new(exampleRoot, anchor.File, anchor.Line);
        if (landmarks.TryGetValue(key, out Landmark? landmark))
        {
            if (landmark.CommittedLine < 1 || landmark.CommittedLine > lines.Count)
                return Unresolved(
                    anchor,
                    $"landmark committed line {landmark.CommittedLine} is beyond end of file ({lines.Count} lines)");

            string committed = lines[landmark.CommittedLine - 1];
            if (committed.Contains(landmark.Snippet, StringComparison.Ordinal)) return new AnchorResult(AnchorBucket.Landmark, null);

            return Unresolved(
                anchor,
                $"landmark snippet '{landmark.Snippet}' not found on committed line {landmark.CommittedLine} (found: {committed.Trim()})");
        }

        if (anchor.Line < 1 || anchor.Line > lines.Count) return Unresolved(anchor, $"source line {anchor.Line} is beyond end of file ({lines.Count} lines) and no landmark pins it");

        return Unresolved(
            anchor,
            $"expected token '{token}' on line {anchor.Line} but found: {lines[anchor.Line - 1].Trim()}");
    }

    /// <summary>
    ///     A line reader rooted at <paramref name="repoRoot" />: it maps a repository-relative path to its
    ///     lines, or <see langword="null" /> when the file does not exist. It reads whole lines, so a
    ///     trailing newline never presents a phantom empty final line to a beyond-end-of-file check. Each
    ///     reader remembers every path it has answered for, absent ones included: one walkthrough anchors
    ///     dozens of lines into the same few files, and each anchor is classified more than once per run.
    /// </summary>
    public static Func<string, IReadOnlyList<string>?> DiskReader(string repoRoot)
    {
        Dictionary<string, IReadOnlyList<string>?> read = new(StringComparer.Ordinal);

        return repoRelative =>
        {
            if (read.TryGetValue(repoRelative, out IReadOnlyList<string>? cached)) return cached;

            string full = Path.Combine(repoRoot, repoRelative.Replace('/', Path.DirectorySeparatorChar));
            IReadOnlyList<string>? lines = File.Exists(full) ? File.ReadAllLines(full) : null;
            read[repoRelative] = lines;

            return lines;
        };
    }

    /// <summary>
    ///     Composes the repository-relative path an anchor resolves against. An empty
    ///     <paramref name="exampleRoot" /> means the anchoring doc quotes this repository's own sources, so
    ///     the anchor's path is already repository-relative and is returned unchanged: prefixing a separator
    ///     would yield a rooted path, which <see cref="Path.Combine(string,string)" /> resolves outside the
    ///     repository and would silently take every such anchor out of the gate's reach.
    /// </summary>
    private static string RepoRelative(string exampleRoot, string file)
    {
        return exampleRoot.Length == 0 ? file : $"{exampleRoot}/{file}";
    }

    private static AnchorResult Unresolved(SourceAnchor anchor, string reason)
    {
        return new AnchorResult(AnchorBucket.Unresolved,
            $"{anchor.Doc}:{anchor.DocLine} -> {anchor.File}:{anchor.Line} ({anchor.Message}): {reason}.");
    }

    private static bool TryVerbTarget(string message, string verb, out string target)
    {
        var key = $" {verb} ";
        int index = message.IndexOf(key, StringComparison.Ordinal);
        if (index < 0)
        {
            target = string.Empty;
            return false;
        }

        target = message[(index + key.Length)..]
            .Trim();
        return true;
    }

    private static string StripGeneric(string text)
    {
        int index = text.IndexOf('<');
        return index < 0 ? text : text[..index];
    }

    private static string LastSegment(string dotted)
    {
        int index = dotted.LastIndexOf('.');
        return index < 0 ? dotted : dotted[(index + 1)..];
    }

    private static string LastTwoSegments(string dotted)
    {
        string[] segments = dotted.Split('.');
        return segments.Length >= 2
            ? $"{segments[^2]}.{segments[^1]}"
            : dotted;
    }

    /// <summary>What the fence scan made of one line.</summary>
    private enum LineKind
    {
        /// <summary>Outside every fence.</summary>
        Prose,

        /// <summary>The delimiter line that opens a fence.</summary>
        FenceOpen,

        /// <summary>The delimiter line that closes a fence.</summary>
        FenceClose,

        /// <summary>Inside a fence: quoted content.</summary>
        Fenced
    }

    /// <summary>
    ///     Every line of <paramref name="docText" /> in document order, each labelled with what the scan
    ///     made of it. A fence opens on a line whose first non-whitespace content is a run of three or more
    ///     backticks or tildes and closes on the next line with at least as long a run of the same
    ///     character and nothing after it but whitespace; an unclosed fence holds every line to the end of
    ///     the text. Input newlines are normalized to <c>"\n"</c> first, so a <c>\r</c> never reaches a
    ///     caller.
    /// </summary>
    /// <remarks>
    ///     This is the one fence scanner the quote gates share, and the only place the state machine
    ///     lives; every other view of a doc here is a filter over it.
    /// </remarks>
    private static IEnumerable<(string Text, int Number, LineKind Kind)> Scan(string docText)
    {
        string[] lines = docText.NormalizedLines()
            .Split('\n');
        var insideFence = false;
        var fenceChar = '\0';
        var fenceLength = 0;

        for (var index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            int number = index + 1;

            if (!insideFence)
            {
                if (TryOpenFence(line, out fenceChar, out fenceLength))
                {
                    insideFence = true;
                    yield return (line, number, LineKind.FenceOpen);
                }
                else
                {
                    yield return (line, number, LineKind.Prose);
                }
            }
            else if (ClosesFence(line, fenceChar, fenceLength))
            {
                insideFence = false;
                fenceChar = '\0';
                fenceLength = 0;
                yield return (line, number, LineKind.FenceClose);
            }
            else
            {
                yield return (line, number, LineKind.Fenced);
            }
        }
    }

    private static bool TryOpenFence(string line, out char fenceChar, out int fenceLength)
    {
        fenceChar = '\0';
        fenceLength = 0;
        int index = SkipLeadingWhitespace(line);
        if (index >= line.Length) return false;

        char candidate = line[index];
        if (candidate != '`' && candidate != '~') return false;

        int run = RunLength(line, index, candidate);
        if (run < 3) return false;

        fenceChar = candidate;
        fenceLength = run;
        return true;
    }

    private static bool ClosesFence(string line, char fenceChar, int minimumLength)
    {
        int index = SkipLeadingWhitespace(line);
        int run = RunLength(line, index, fenceChar);
        if (run < minimumLength) return false;

        int afterRun = index + run;
        while (afterRun < line.Length)
        {
            if (line[afterRun] != ' ' && line[afterRun] != '\t') return false;

            afterRun++;
        }

        return true;
    }

    private static int SkipLeadingWhitespace(string line)
    {
        var index = 0;
        while (index < line.Length && (line[index] == ' ' || line[index] == '\t')) index++;

        return index;
    }

    private static int RunLength(string line, int start, char character)
    {
        int index = start;
        while (index < line.Length && line[index] == character) index++;

        return index - start;
    }

    /// <summary>The landmark lookup key: an anchor's example root, source file, and reported line.</summary>
    public readonly record struct AnchorKey(string ExampleRoot, string File, int Line);

    /// <summary>
    ///     A landmark pin. <see cref="CommittedLine" /> is the committed source line the documented
    ///     walkthrough edit is keyed to (the anchor's reported line is the post-edit position, which need
    ///     not equal it), and <see cref="Snippet" /> is text that committed line must still contain.
    /// </summary>
    public sealed record Landmark(int CommittedLine, string Snippet);

    /// <summary>
    ///     The outcome of classifying one anchor: the bucket that resolved it, and — when it is
    ///     <see cref="AnchorBucket.Unresolved" /> — the human-readable failure describing what was expected.
    /// </summary>
    public sealed record AnchorResult(AnchorBucket Bucket, string? Failure);
}
