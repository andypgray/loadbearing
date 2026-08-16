using System.Text.RegularExpressions;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     The write-report checker the quoted-path gate runs on. A hand-written walkthrough quotes captured
///     <c>render</c> and <c>baseline</c> output, and every file those verbs touch is reported as
///     <c>wrote &lt;path&gt;</c> or <c>unchanged &lt;path&gt;</c> — the path spelled relative to the
///     solution the verb ran against. This lifts every such line out of a doc's fenced code blocks and
///     decides whether the file it names is still a file this repository publishes. All of the logic
///     lives here so the gate and the unit tests exercise the same code path.
/// </summary>
/// <remarks>
///     <para>
///         <b>Tracked, not merely present.</b> The claim a walkthrough makes is that running the verb on
///         a checkout produces these lines, and a checkout holds exactly what git tracks — so an
///         untracked file on the author's disk must not answer for a quoted path. That also makes the
///         gate agree with the render-diff CI leg, which compares the committed tree.
///     </para>
///     <para>
///         <b>The path token must look like a path.</b> A fenced prose line reading
///         <c>unchanged behaviour</c> matches the verb and the token shape but names no file; requiring a
///         <c>.</c> or a <c>/</c> in the token keeps it out rather than reporting it as a stranded path.
///     </para>
///     <para>
///         <b>Committed bytes only.</b> Like its sibling quote gates this reads the tracked file list and
///         nothing else: no workspace load and no CLI invocation, so it stays cheap, cross-platform and
///         parallel-safe.
///     </para>
/// </remarks>
internal static class WrittenPaths
{
    /// <summary>Whether a quoted write-report path still names a file this repository publishes.</summary>
    public enum PathBucket
    {
        /// <summary>The path resolves to a tracked file under the doc's example root.</summary>
        Matched,

        /// <summary>Nothing git tracks sits at the path, so the quoted line names a file that is gone.</summary>
        Untracked
    }

    /// <summary>
    ///     A write-report line: the verb, a single space, then the solution-relative path. The path is a
    ///     whitespace-free token running to the end of the line, which is what
    ///     <c>WriteReport.Line</c> emits.
    /// </summary>
    private static readonly Regex ReportLine =
        new(@"^\s*(?<label>wrote|unchanged) (?<path>\S+)$", RegexOptions.CultureInvariant);

    /// <summary>
    ///     Extracts every write-report line from the fenced code blocks of <paramref name="docText" />.
    ///     Fence state is tracked by <see cref="SourceAnchors.FencedLines" />, so the same words in running
    ///     prose are ignored: prose restates the captured output and is commentary rather than the quote.
    ///     Line numbers are 1-based positions in the doc, so a failure names the exact line.
    /// </summary>
    public static IReadOnlyList<WrittenPath> Extract(string doc, string docText)
    {
        List<WrittenPath> written = new();

        foreach ((string line, int number) in SourceAnchors.FencedLines(docText))
        {
            Match match = ReportLine.Match(line);
            if (!match.Success) continue;

            string path = match.Groups["path"]
                .Value;
            if (!LooksLikeAPath(path)) continue;

            written.Add(new WrittenPath(
                doc,
                number,
                match.Groups["label"]
                    .Value,
                path));
        }

        return written;
    }

    /// <summary>
    ///     Composes the repository-relative path a quoted write report resolves against. The verbs report
    ///     paths relative to the solution directory, so an example's walkthrough hangs its paths off that
    ///     example's root; an empty <paramref name="exampleRoot" /> means the doc quotes a run over this
    ///     repository's own solution and the path is already repository-relative.
    /// </summary>
    public static string RepoRelative(string exampleRoot, string path)
    {
        return exampleRoot.Length == 0 ? path : $"{exampleRoot}/{path}";
    }

    /// <summary>
    ///     Decides whether <paramref name="written" /> still names a published file: matched when the
    ///     repository-relative path is in <paramref name="trackedPaths" />, untracked otherwise, with a
    ///     failure naming the doc, the line and the path that was looked for.
    /// </summary>
    public static PathResult Classify(WrittenPath written, string exampleRoot, IReadOnlySet<string> trackedPaths)
    {
        string repoRelative = RepoRelative(exampleRoot, written.Path);
        if (trackedPaths.Contains(repoRelative)) return new PathResult(PathBucket.Matched, null);

        return new PathResult(
            PathBucket.Untracked,
            $"{written.Doc}:{written.DocLine} reports '{written.Label} {written.Path}' -> {repoRelative}, which git does not track.");
    }

    /// <summary>
    ///     The tracked paths as an ordinal lookup set. Path comparison is ordinal throughout, because a
    ///     case-insensitive match would let a rename that only changes case pass on Windows and fail on
    ///     the CI legs that do not.
    /// </summary>
    public static IReadOnlySet<string> TrackedSet(IEnumerable<string> trackedPaths)
    {
        return trackedPaths.ToHashSet(StringComparer.Ordinal);
    }

    private static bool LooksLikeAPath(string token)
    {
        return token.Contains('.', StringComparison.Ordinal) || token.Contains('/', StringComparison.Ordinal);
    }

    /// <summary>
    ///     The outcome of classifying one quoted path: the bucket it fell in, and — when that bucket is
    ///     not <see cref="PathBucket.Matched" /> — the human-readable failure describing what was missing.
    /// </summary>
    public sealed record PathResult(PathBucket Bucket, string? Failure);
}
