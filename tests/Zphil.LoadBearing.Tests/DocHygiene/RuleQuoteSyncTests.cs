using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     The quote-sync gate over quoted rule sentences. The example walkthroughs and the root README
///     quote captured <c>check</c> output, and every stanza opens with a rule-header line carrying the
///     sentence that rule renders — the same sentence <c>render</c> writes into the <c>AGENTS.md</c>
///     beside it. Nothing regenerates a hand-written doc, so this gate holds each quoted sentence to the
///     committed block it was copied from: a spec edit that changes a rendered sentence and leaves a
///     walkthrough behind fails the suite instead of publishing a README that contradicts the
///     <c>AGENTS.md</c> next to it. Two quotes are narrative — output from a draft spec, or from a check
///     over a test fixture that renders no block — and are pinned as such, so the day one starts matching
///     it is promoted rather than quietly counted as covered.
/// </summary>
/// <remarks>
///     Sibling to <see cref="QuoteSyncTests" />, which covers the other quote shape: a whole fenced block
///     copied from a committed file, held line by line. A rule sentence cannot be held that way because a
///     <c>Migrate</c> rule renders it mid-bullet, wrapped in the old-pattern narration, so this gate
///     compares by ordinal containment against the bullet for its id. Like its sibling it reads only
///     committed bytes: no workspace load, no CLI invocation, so it stays cheap and parallel-safe.
/// </remarks>
public sealed class RuleQuoteSyncTests
{
    private const string RootReadme = "README.md";
    private const string MeridianAdopting = "examples/Meridian/ADOPTING.md";

    /// <summary>
    ///     This repository's own root, the bullet base for docs quoting a check over this solution rather
    ///     than over an example. Empty because its <c>AGENTS.md</c> files are the ones outside
    ///     <c>examples/</c>.
    /// </summary>
    private const string SelfRoot = "";

    /// <summary>The rule-quoting docs, each paired with the example root whose rendered bullets it quotes.</summary>
    private static readonly (string Doc, string ExampleRoot)[] QuoteDocs =
    [
        ("examples/Meridian/README.md", "examples/Meridian"),
        (MeridianAdopting, "examples/Meridian"),
        ("examples/Meridian/hooks/storyboard.md", "examples/Meridian"),
        ("examples/Meridian.Quoting/README.md", "examples/Meridian.Quoting"),
        ("examples/Meridian.Operations/README.md", "examples/Meridian.Operations"),
        ("examples/Meridian.Interchange/README.md", "examples/Meridian.Interchange"),
        // The root README's dogfood spine quotes a self-check over this solution, so its sentences come
        // from this repository's own rendered block and scoped cards.
        (RootReadme, SelfRoot)
    ];

    /// <summary>
    ///     Narrative quotes: rule-header lines whose sentence is deliberately not what any committed block
    ///     renders. They must stay unmatched — the day one starts matching, the reason it was exempt has
    ///     gone away and the entry should be deleted so the quote joins the gate. Keyed by sentence rather
    ///     than by line so an edit above the quote does not break the pin, and so the same rule quoted
    ///     elsewhere in the same doc stays covered.
    /// </summary>
    private static readonly (string Doc, string RuleId, string Sentence)[] NarrativeQuotes =
    [
        // ADOPTING.md walks the spec being written. Step 4 runs the rule before step 5 grows it the
        // `.Except(SystemClock)` carve-out, so this is the draft's output, not the committed spec's; the
        // draft it is cut from is quoted in the same doc. The finished rule's sentence is quoted later in
        // the same walkthrough and is held by the gate.
        (MeridianAdopting,
            "time/inject-clock",
            "The Web layer must not use `DateTime.Now` or `DateTime.UtcNow`."),

        // The root README's Framework stanza is a check over the non-SDK-style ClassicApp test fixture,
        // whose spec renders no AGENTS.md at all — the point of that stanza is that the tool reaches a
        // codebase that has no generated context. ClassicProjectCheckTests pins its lines instead.
        (RootReadme,
            "data-access/no-inline-sql",
            "Types in `Classic.*` must not reference types in `System.Data.*`.")
    ];

    [Fact]
    public void QuotedRuleSentences_MatchTheirRenderedBullet()
    {
        // Arrange
        List<string> drift = new();

        // Act: every quoted sentence that is not a documented narrative must appear, ordinally, inside a
        // bullet its own example root renders for that rule id.
        foreach ((string doc, string exampleRoot) in QuoteDocs)
        {
            IReadOnlyDictionary<string, IReadOnlyList<string>> bullets = BulletsFor(exampleRoot);
            foreach (RuleQuote quote in ExtractDoc(doc))
            {
                if (IsNarrative(quote)) continue;

                RuleQuotes.QuoteResult result = RuleQuotes.Classify(quote, bullets);
                if (result.Bucket != RuleQuotes.QuoteBucket.Matched) drift.Add(result.Failure!);
            }
        }

        // Assert
        drift.ShouldBeEmpty(
            $"Quoted rule sentences no longer match what the spec renders:\n{string.Join("\n", drift)}");
    }

    [Fact]
    public void NarrativeRuleQuotes_DoNotMatchTheirRenderedBullet()
    {
        // Arrange
        List<string> promoted = new();

        // Act: a narrative quote is exempt because no committed block renders its sentence; if one starts
        // matching, the exemption is dead and the quote should be held by the gate above.
        foreach ((string doc, string exampleRoot) in QuoteDocs)
        {
            IReadOnlyDictionary<string, IReadOnlyList<string>> bullets = BulletsFor(exampleRoot);
            foreach (RuleQuote quote in ExtractDoc(doc))
            {
                if (!IsNarrative(quote)) continue;

                RuleQuotes.QuoteResult result = RuleQuotes.Classify(quote, bullets);
                if (result.Bucket == RuleQuotes.QuoteBucket.Matched) promoted.Add($"{quote.Doc}:{quote.DocLine} quotes '{quote.RuleId}' as a narrative, but the rendered bullet now carries that sentence; remove the exemption.");
            }
        }

        // Assert
        promoted.ShouldBeEmpty(
            $"Narrative rule quotes now match what the spec renders:\n{string.Join("\n", promoted)}");
    }

    [Fact]
    public void EveryRuleQuoteDoc_YieldsAtLeastOneQuote()
    {
        // Arrange
        List<string> empty = new();

        // Act: guard against the scanner silently matching nothing if a doc's quoting style changes.
        foreach ((string doc, string _) in QuoteDocs)
            if (ExtractDoc(doc)
                    .Count == 0)
                empty.Add(doc);

        // Assert
        empty.ShouldBeEmpty(
            $"These docs yielded no rule quotes; the scanner may be silently matching nothing:\n{string.Join("\n", empty)}");
    }

    [Fact]
    public void NarrativeEntries_AllMatchAnExtractedQuote()
    {
        // Arrange
        HashSet<(string Doc, string RuleId, string Sentence)> extracted = new();
        foreach ((string doc, string _) in QuoteDocs)
        foreach (RuleQuote quote in ExtractDoc(doc))
            extracted.Add((quote.Doc, quote.RuleId, quote.Sentence));

        // Act
        List<string> dead = new();
        foreach ((string Doc, string RuleId, string Sentence) narrative in NarrativeQuotes)
            if (!extracted.Contains(narrative))
                dead.Add($"narrative {narrative.Doc} -> {narrative.RuleId} ('{narrative.Sentence}') matches no extracted quote.");

        // Assert
        dead.ShouldBeEmpty($"These narrative entries no longer correspond to any quote and should be removed:\n{string.Join("\n", dead)}");
    }

    [Fact]
    public void EveryTrackedDocQuotingRules_IsRegistered()
    {
        // Arrange: the registry above is hand-written, so the failure it cannot see is a doc that quotes
        // rules and was never added to it — a whole example silently outside the gate. Git decides the
        // scope, as it does for every hygiene gate here, and the sweep runs over every tracked markdown
        // file rather than only the ones under examples/: the root README is already registered, so
        // restricting it to examples/ would leave that doc's siblings unguarded for no reason.
        HashSet<string> registered = QuoteDocs
            .Select(entry => entry.Doc)
            .ToHashSet(StringComparer.Ordinal);
        List<string> unregistered = new();

        // Act
        foreach (string path in TrackedFiles.Markdown)
        {
            if (registered.Contains(path)) continue;

            int quotes = ExtractDoc(path)
                .Count;
            if (quotes > 0) unregistered.Add($"{path} quotes {quotes} rule sentence(s) but is not registered.");
        }

        // Assert
        unregistered.ShouldBeEmpty(
            $"These tracked docs quote rule sentences that nothing holds to the spec:\n{string.Join("\n", unregistered)}");
    }

    private static bool IsNarrative(RuleQuote quote)
    {
        return NarrativeQuotes.Contains((quote.Doc, quote.RuleId, quote.Sentence));
    }

    private static IReadOnlyList<RuleQuote> ExtractDoc(string doc)
    {
        string text = RepoRoot.ReadText(doc);
        return RuleQuotes.Extract(doc, text);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BulletsFor(string exampleRoot)
    {
        return RuleQuotes.RenderedBullets(exampleRoot, TrackedFiles.All, RuleQuotes.DiskTextReader(RepoRoot.Directory));
    }
}
