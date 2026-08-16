using System.Text.RegularExpressions;
using Zphil.LoadBearing.Rendering;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     The rule-quote checker the quoted-sentence gate runs on. A hand-written doc quotes captured
///     <c>check</c> output, and each stanza opens with a rule-header line — <c>pass|FAIL|warn|skip</c>,
///     the rule id, an em dash, then the sentence the spec renders for that rule. This lifts every such
///     line out of a doc's fenced code blocks, indexes the rule sentences the committed <c>AGENTS.md</c>
///     files under an example root actually render, and decides whether each quoted sentence still
///     agrees with them. All of the logic lives here so the gate and the unit tests exercise the same
///     code path.
/// </summary>
/// <remarks>
///     <para>
///         <b>Containment, not equality.</b> A <c>Migrate</c> rule renders as
///         <c>
///             Most existing code here
///             follows the OLD pattern: … New code must follow: &lt;sentence&gt; …
///         </c>
///         , so the quoted sentence
///         sits mid-bullet rather than at its start. The comparison is therefore an ordinal substring of
///         the bullet's text — the same primitive the excerpt gate's in-order line check uses.
///     </para>
///     <para>
///         <b>Status lines are deliberately out of scope.</b>
///         <c>
///             pass time/inject-clock (migrate) — 7
///             grandfathered remaining, 0 new, 0 fixed awaiting acceptance
///         </c>
///         carries counts rather than a
///         rendered sentence, and the <c> (posture)</c> infix between the id and the em dash is what keeps
///         it out of the pattern. Holding those counts to the baselines they come from is a different
///         invariant against a different source.
///     </para>
///     <para>
///         <b>Committed bytes only.</b> This reads the generated files git tracks, never the renderer:
///         that the committed <c>AGENTS.md</c> still matches what the spec emits is proved by the dogfood
///         self-spec tests and by CI re-rendering the examples and failing on any diff. Splitting it that
///         way keeps this gate free of a workspace load, so it stays cheap, cross-platform and
///         parallel-safe.
///     </para>
/// </remarks>
internal static class RuleQuotes
{
    /// <summary>Whether a quoted rule sentence still agrees with the rendered bullet for its id.</summary>
    public enum QuoteBucket
    {
        /// <summary>A bullet for the quoted id renders a text containing the quoted sentence.</summary>
        Matched,

        /// <summary>No committed bullet renders the quoted rule id at all.</summary>
        Unrendered,

        /// <summary>A bullet renders the id, but none of them carries the quoted sentence.</summary>
        Drifted
    }

    private const char EmDash = (char)0x2014;

    private const string AgentsFileName = "AGENTS.md";

    /// <summary>
    ///     A rule-header line: the status verb, the rule id, an em dash, then the rendered sentence. The
    ///     id allows more than two segments, because a scoped rule renders as
    ///     <c>clearance/engine/containment</c> and a single-slash pattern would silently drop every one of
    ///     them.
    /// </summary>
    private static readonly Regex HeaderLine =
        new($@"^\s*(?<status>pass|FAIL|warn|skip) (?<id>[a-z][a-z0-9.-]*(?:/[a-z0-9-]+)+) {EmDash} (?<sentence>.+)$",
            RegexOptions.CultureInvariant);

    /// <summary>A rendered rule bullet inside a managed block: <c>- `id` — text</c>.</summary>
    private static readonly Regex BulletLine =
        new($@"^- `(?<id>[^`]+)` {EmDash} (?<text>.+)$", RegexOptions.CultureInvariant);

    /// <summary>
    ///     Extracts every rule-header line from the fenced code blocks of <paramref name="docText" />.
    ///     Fence state is tracked by <see cref="SourceAnchors.Fences" />'s scanner, so a rule-header line
    ///     in running prose is ignored: prose restates the quoted output and is commentary rather than the
    ///     quote. Line numbers are 1-based positions in the doc, so a failure names the exact line.
    /// </summary>
    public static IReadOnlyList<RuleQuote> Extract(string doc, string docText)
    {
        List<RuleQuote> quotes = new();

        foreach ((string line, int number) in SourceAnchors.FencedLines(docText))
        {
            Match match = HeaderLine.Match(line);
            if (!match.Success) continue;

            quotes.Add(new RuleQuote(
                doc,
                number,
                match.Groups["status"]
                    .Value,
                match.Groups["id"]
                    .Value,
                match.Groups["sentence"]
                    .Value.Trim()));
        }

        return quotes;
    }

    /// <summary>
    ///     Indexes the rule bullets rendered by the managed blocks of <paramref name="cardTexts" />, one
    ///     entry per rule id holding every text rendered for it. An id can render more than once — a rule
    ///     appears in the root block and again on the scoped card of the layer it covers — so a quoted
    ///     sentence matches when <em>any</em> of them carries it. A text with no managed block contributes
    ///     nothing.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> IndexBullets(IEnumerable<string> cardTexts)
    {
        Dictionary<string, List<string>> index = new(StringComparer.Ordinal);

        foreach (string cardText in cardTexts)
        {
            string? body = ManagedBlock.ExtractBody(cardText);
            if (body is null) continue;

            foreach (string line in body.Split('\n'))
            {
                Match match = BulletLine.Match(line.TrimEnd('\r'));
                if (!match.Success) continue;

                string id = match.Groups["id"]
                    .Value;
                if (!index.TryGetValue(id, out var texts))
                {
                    texts = new List<string>();
                    index[id] = texts;
                }

                texts.Add(match.Groups["text"]
                    .Value);
            }
        }

        return index.ToDictionary(
            static entry => entry.Key,
            static entry => (IReadOnlyList<string>)entry.Value,
            StringComparer.Ordinal);
    }

    /// <summary>
    ///     The tracked <c>AGENTS.md</c> paths whose rendered bullets a doc under
    ///     <paramref name="exampleRoot" /> is quoting — the example's root block and every scoped card
    ///     beneath it. An empty <paramref name="exampleRoot" /> means this repository's own spec, so the
    ///     examples are excluded: they are rendered by a different spec, and letting their bullets answer
    ///     for the root README would resolve a quote against a rule the self-spec never rendered.
    /// </summary>
    public static IReadOnlyList<string> RenderedCards(string exampleRoot, IEnumerable<string> trackedPaths)
    {
        return trackedPaths
            .Where(path => path.EndsWith($"/{AgentsFileName}", StringComparison.Ordinal) || path == AgentsFileName)
            .Where(path => exampleRoot.Length == 0
                ? !path.StartsWith("examples/", StringComparison.Ordinal)
                : path.StartsWith($"{exampleRoot}/", StringComparison.Ordinal))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    ///     The rendered-bullet index for <paramref name="exampleRoot" />: the managed-block bullets of
    ///     every tracked <c>AGENTS.md</c> under it, keyed by rule id. <paramref name="readText" /> reads a
    ///     repository-relative path's whole text, or returns <see langword="null" /> when it is absent.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> RenderedBullets(
        string exampleRoot,
        IEnumerable<string> trackedPaths,
        Func<string, string?> readText)
    {
        var texts = RenderedCards(exampleRoot, trackedPaths)
            .Select(readText)
            .OfType<string>();

        return IndexBullets(texts);
    }

    /// <summary>
    ///     Decides whether <paramref name="quote" /> still agrees with what <paramref name="bullets" />
    ///     renders for its rule id: matched when some bullet for that id contains the quoted sentence
    ///     ordinally, unrendered when no bullet carries the id, drifted when one does but none carries the
    ///     sentence. Both failing buckets name the doc, the line, the id and — for a drift — the sentence
    ///     quoted alongside what is rendered instead.
    /// </summary>
    public static QuoteResult Classify(RuleQuote quote, IReadOnlyDictionary<string, IReadOnlyList<string>> bullets)
    {
        if (!bullets.TryGetValue(quote.RuleId, out var texts))
            return new QuoteResult(
                QuoteBucket.Unrendered,
                $"{quote.Doc}:{quote.DocLine} quotes rule '{quote.RuleId}', which no committed AGENTS.md renders.");

        if (texts.Any(text => text.Contains(quote.Sentence, StringComparison.Ordinal))) return new QuoteResult(QuoteBucket.Matched, null);

        return new QuoteResult(
            QuoteBucket.Drifted,
            $"{quote.Doc}:{quote.DocLine} quotes '{quote.RuleId}' as:\n    {quote.Sentence}\n  but the rendered bullet reads:\n    {string.Join("\n    ", texts)}");
    }

    /// <summary>
    ///     A whole-text reader rooted at <paramref name="repoRoot" />: it maps a repository-relative path
    ///     to its text, or <see langword="null" /> when the file does not exist.
    /// </summary>
    public static Func<string, string?> DiskTextReader(string repoRoot)
    {
        return repoRelative =>
        {
            string full = Path.Combine(repoRoot, repoRelative.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(full) ? File.ReadAllText(full) : null;
        };
    }

    /// <summary>
    ///     The outcome of classifying one quote: the bucket it fell in, and — when that bucket is not
    ///     <see cref="QuoteBucket.Matched" /> — the human-readable failure describing the disagreement.
    /// </summary>
    public sealed record QuoteResult(QuoteBucket Bucket, string? Failure);
}
