using System.Text.RegularExpressions;

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
    ///     Extracts every anchor from the fenced code blocks of <paramref name="docText" />. Fence state
    ///     is tracked exactly as the prose checker strips fences — a fence opens on a line whose first
    ///     non-whitespace content is a run of three or more backticks or tildes and closes on the next
    ///     line with at least as long a run of the same character and nothing after it but whitespace —
    ///     but here the fenced content is kept and scanned. Anchors outside fences are ignored, because
    ///     inline prose short-forms restate the fenced anchors and are commentary rather than the pin.
    /// </summary>
    public static IReadOnlyList<SourceAnchor> Extract(string doc, string docText)
    {
        string normalized = docText.Replace("\r\n", "\n");
        string[] lines = normalized.Split('\n');
        List<SourceAnchor> anchors = new();
        var insideFence = false;
        var fenceChar = '\0';
        var fenceLength = 0;

        for (var index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            if (!insideFence)
            {
                if (TryOpenFence(line, out fenceChar, out fenceLength)) insideFence = true;
            }
            else if (ClosesFence(line, fenceChar, fenceLength))
            {
                insideFence = false;
                fenceChar = '\0';
                fenceLength = 0;
            }
            else
            {
                Match match = AnchorLine.Match(line);
                if (match.Success)
                {
                    string file = match.Groups[1].Value;
                    int sourceLine = int.Parse(match.Groups[2].Value);
                    string message = match.Groups[3].Value.Trim();
                    anchors.Add(new SourceAnchor(doc, index + 1, file, sourceLine, message));
                }
            }
        }

        return anchors;
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
    ///     source line, and what was expected. <paramref name="readLines" /> reads a repository-relative
    ///     path into its lines, or returns <see langword="null" /> when the file is absent.
    /// </summary>
    public static AnchorResult Classify(
        SourceAnchor anchor,
        string exampleRoot,
        IReadOnlyDictionary<AnchorKey, Landmark> landmarks,
        Func<string, IReadOnlyList<string>?> readLines)
    {
        var repoRelativeFile = $"{exampleRoot}/{anchor.File}";
        var lines = readLines(repoRelativeFile);
        if (lines is null) return Unresolved(anchor, $"source file not found at {repoRelativeFile}");

        string token = DeriveToken(anchor.Message);
        bool contentMatch = anchor.Line >= 1
                            && anchor.Line <= lines.Count
                            && lines[anchor.Line - 1].Contains(token, StringComparison.Ordinal);
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
    ///     Verifies every anchor in <paramref name="docs" /> against the committed sources under
    ///     <paramref name="repoRoot" />, returning one human-readable failure per unresolved anchor (an
    ///     empty list is green). Each doc is paired with the example root its anchors resolve against.
    /// </summary>
    public static IReadOnlyList<string> Verify(
        string repoRoot,
        IReadOnlyList<(string Doc, string ExampleRoot)> docs,
        IReadOnlyDictionary<AnchorKey, Landmark> landmarks)
    {
        var reader = DiskReader(repoRoot);
        List<string> failures = new();

        foreach ((string doc, string exampleRoot) in docs)
        {
            string docPath = Path.Combine(repoRoot, doc.Replace('/', Path.DirectorySeparatorChar));
            string text = File.ReadAllText(docPath);
            foreach (SourceAnchor anchor in Extract(doc, text))
            {
                AnchorResult result = Classify(anchor, exampleRoot, landmarks, reader);
                if (result.Bucket == AnchorBucket.Unresolved) failures.Add(result.Failure!);
            }
        }

        return failures;
    }

    /// <summary>
    ///     A line reader rooted at <paramref name="repoRoot" />: it maps a repository-relative path to its
    ///     lines, or <see langword="null" /> when the file does not exist. It reads whole lines, so a
    ///     trailing newline never presents a phantom empty final line to a beyond-end-of-file check.
    /// </summary>
    public static Func<string, IReadOnlyList<string>?> DiskReader(string repoRoot)
    {
        return repoRelative =>
        {
            string full = Path.Combine(repoRoot, repoRelative.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(full) ? File.ReadAllLines(full) : null;
        };
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

        target = message[(index + key.Length)..].Trim();
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