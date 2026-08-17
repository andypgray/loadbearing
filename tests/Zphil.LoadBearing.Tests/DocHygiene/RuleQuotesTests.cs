using Shouldly;
using Xunit;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     Unit and negative tests for <see cref="RuleQuotes" />. They pin the fenced rule-header scanner —
///     including the multi-segment ids and the status lines it must leave alone — the managed-block
///     bullet index, and the classifier that decides whether a quoted sentence still agrees with what the
///     spec renders, so the gate and these tests exercise the same code path.
/// </summary>
public sealed class RuleQuotesTests
{
    private const char EmDash = (char)0x2014;

    [Fact]
    public void Extract_FencedRuleHeader_CapturedWithLocationAndParts()
    {
        // Arrange
        string[] lines =
        [
            "intro prose",
            "```text",
            $"FAIL data-access/no-inline-sql {EmDash} Types in `Web.*` must not reference `SqlConnection`.",
            "  because: SQL in the request path cannot be faked.",
            "```",
            $"pass layering/domain-independent {EmDash} The Domain layer must not reference the Web layer."
        ];
        string doc = string.Join("\n", lines);

        // Act
        IReadOnlyList<RuleQuote> quotes = RuleQuotes.Extract("d.md", doc);

        // Assert: the line outside the fence is prose restating the quote, not the quote itself.
        RuleQuote quote = quotes.ShouldHaveSingleItem();
        quote.Doc.ShouldBe("d.md");
        quote.DocLine.ShouldBe(3);
        quote.Status.ShouldBe("FAIL");
        quote.RuleId.ShouldBe("data-access/no-inline-sql");
        quote.Sentence.ShouldBe("Types in `Web.*` must not reference `SqlConnection`.");
    }

    [Fact]
    public void Extract_MultiSegmentRuleId_Captured()
    {
        // Arrange: a scoped rule renders a third segment, and a single-slash pattern would drop every
        // containment rule in the corpus.
        string doc = string.Join(
            "\n",
            "```text",
            $"pass clearance/engine/containment {EmDash} Types in `Meridian.Clearance.*` must be contained.",
            "```");

        // Act
        IReadOnlyList<RuleQuote> quotes = RuleQuotes.Extract("d.md", doc);

        // Assert
        quotes.ShouldHaveSingleItem()
            .RuleId.ShouldBe("clearance/engine/containment");
    }

    [Theory]
    [InlineData("pass")]
    [InlineData("FAIL")]
    [InlineData("warn")]
    [InlineData("skip")]
    public void Extract_EveryStatusVerb_Captured(string status)
    {
        // Arrange
        string doc = string.Join(
            "\n",
            "```text",
            $"{status} naming/controllers {EmDash} Types derived from `ControllerBase` must be named `*Controller`.",
            "```");

        // Act
        IReadOnlyList<RuleQuote> quotes = RuleQuotes.Extract("d.md", doc);

        // Assert
        quotes.ShouldHaveSingleItem()
            .Status.ShouldBe(status);
    }

    [Fact]
    public void Extract_StatusLineWithPostureInfix_Ignored()
    {
        // Arrange: `status` output carries counts rather than a rendered sentence, and the ` (posture)`
        // infix between the id and the em dash is what keeps it out of the pattern. Holding those counts
        // to the baselines they come from is a different invariant against a different source.
        string doc = string.Join(
            "\n",
            "```text",
            $"pass time/inject-clock (migrate) {EmDash} 7 grandfathered remaining, 0 new, 0 fixed awaiting acceptance",
            "```");

        // Act
        IReadOnlyList<RuleQuote> quotes = RuleQuotes.Extract("d.md", doc);

        // Assert
        quotes.ShouldBeEmpty();
    }

    [Fact]
    public void Extract_MultipleFencedBlocks_AllQuotesCaptured()
    {
        // Arrange
        string[] lines =
        [
            "```text",
            $"  pass http/reuse-httpclient {EmDash} Types must not construct `HttpClient`.",
            "```",
            "prose between blocks",
            "~~~",
            $"FAIL naming/async-suffix {EmDash} Methods returning `Task` must be named `*Async`.",
            "~~~"
        ];
        string doc = string.Join("\n", lines);

        // Act
        IReadOnlyList<RuleQuote> quotes = RuleQuotes.Extract("d.md", doc);

        // Assert
        quotes.Count.ShouldBe(2);
        quotes[0]
            .DocLine.ShouldBe(2);
        quotes[0]
            .RuleId.ShouldBe("http/reuse-httpclient");
        quotes[1]
            .DocLine.ShouldBe(6);
        quotes[1]
            .RuleId.ShouldBe("naming/async-suffix");
    }

    [Fact]
    public void IndexBullets_ManagedBlockBullets_KeyedById()
    {
        // Arrange
        string card = Card(
            $"- `layering/domain-independent` {EmDash} The Domain layer must not reference the Web layer. Domain holds the model.",
            "- Expand any rule above with `loadbearing explain <rule-id>`.");

        // Act
        IReadOnlyDictionary<string, IReadOnlyList<string>> bullets = RuleQuotes.IndexBullets([card]);

        // Assert: the trailing help bullet carries no id and contributes nothing.
        KeyValuePair<string, IReadOnlyList<string>> bullet = bullets.ShouldHaveSingleItem();
        bullet.Key.ShouldBe("layering/domain-independent");
        bullet.Value.ShouldHaveSingleItem()
            .ShouldStartWith("The Domain layer must not reference the Web layer.");
    }

    [Fact]
    public void IndexBullets_SameRuleOnRootBlockAndScopedCard_BothKept()
    {
        // Arrange: a rule renders in the root block and again on the scoped card of the layer it covers,
        // so a quoted sentence matching either one is current.
        string root = Card($"- `time/inject-clock` {EmDash} root wording of the clock rule.");
        string scoped = Card($"- `time/inject-clock` {EmDash} scoped wording of the clock rule.");

        // Act
        IReadOnlyDictionary<string, IReadOnlyList<string>> bullets = RuleQuotes.IndexBullets([root, scoped]);

        // Assert
        bullets["time/inject-clock"]
            .Count.ShouldBe(2);
    }

    [Fact]
    public void IndexBullets_TextWithNoManagedBlock_ContributesNothing()
    {
        // Arrange
        var plain = $"# A hand-written page\n\n- `some/rule` {EmDash} not inside a managed block.";

        // Act
        IReadOnlyDictionary<string, IReadOnlyList<string>> bullets = RuleQuotes.IndexBullets([plain]);

        // Assert
        bullets.ShouldBeEmpty();
    }

    [Fact]
    public void Classify_SentenceInsideBullet_Matched()
    {
        // Arrange: a Migrate rule wraps the sentence in its old-pattern narration, so the quoted sentence
        // sits mid-bullet and the comparison must be containment rather than a prefix.
        RuleQuote quote = new("d.md", 5, "pass", "data-access/no-inline-sql", "Types in `Web.*` must not reference `SqlConnection`.");
        IReadOnlyDictionary<string, IReadOnlyList<string>> bullets = Bullets(
            "data-access/no-inline-sql",
            "Most existing code here follows the OLD pattern: inline SQL. New code must follow: Types in `Web.*` must not reference `SqlConnection`. Data access behind a repository can be swapped.");

        // Act
        RuleQuotes.QuoteResult result = RuleQuotes.Classify(quote, bullets);

        // Assert
        result.Bucket.ShouldBe(RuleQuotes.QuoteBucket.Matched);
        result.Failure.ShouldBeNull();
    }

    [Fact]
    public void Classify_SentenceDriftedFromBullet_Drifted()
    {
        // Arrange
        RuleQuote quote = new("d.md", 5, "FAIL", "time/inject-clock", "The Web layer must not use `DateTime.Now`.");
        IReadOnlyDictionary<string, IReadOnlyList<string>> bullets = Bullets(
            "time/inject-clock",
            "Types in the Web layer, except types whose name matches `SystemClock` must not use `DateTime.Now`.");

        // Act
        RuleQuotes.QuoteResult result = RuleQuotes.Classify(quote, bullets);

        // Assert: the failure names the doc, the line, the rule, and both texts.
        result.Bucket.ShouldBe(RuleQuotes.QuoteBucket.Drifted);
        result.Failure!.ShouldContain("d.md:5");
        result.Failure!.ShouldContain("time/inject-clock");
        result.Failure!.ShouldContain("The Web layer must not use `DateTime.Now`.");
        result.Failure!.ShouldContain("except types whose name matches `SystemClock`");
    }

    [Fact]
    public void Classify_RuleRenderedNowhere_Unrendered()
    {
        // Arrange
        RuleQuote quote = new("d.md", 5, "FAIL", "data-access/no-inline-sql", "Types in `Classic.*` must not reference types in `System.Data.*`.");
        IReadOnlyDictionary<string, IReadOnlyList<string>> bullets = Bullets("layering/domain-independent", "The Domain layer must not reference the Web layer.");

        // Act
        RuleQuotes.QuoteResult result = RuleQuotes.Classify(quote, bullets);

        // Assert
        result.Bucket.ShouldBe(RuleQuotes.QuoteBucket.Unrendered);
        result.Failure!.ShouldContain("no committed AGENTS.md renders");
    }

    [Fact]
    public void RenderedCards_ExampleRoot_TakesThatRootsCardsOnly()
    {
        // Arrange
        string[] tracked =
        [
            "AGENTS.md",
            "examples/Meridian/AGENTS.md",
            "examples/Meridian/src/Meridian.Web/AGENTS.md",
            "examples/Meridian/README.md",
            "examples/Meridian.Quoting/AGENTS.md",
            "src/Zphil.LoadBearing/AGENTS.md"
        ];

        // Act
        IReadOnlyList<string> cards = RuleQuotes.RenderedCards("examples/Meridian", tracked);

        // Assert: the root block and the scoped card beneath it, and nothing from a sibling example whose
        // path shares the prefix as a string but not as a directory.
        cards.ShouldBe(["examples/Meridian/AGENTS.md", "examples/Meridian/src/Meridian.Web/AGENTS.md"]);
    }

    [Fact]
    public void RenderedCards_SelfRoot_ExcludesTheExamples()
    {
        // Arrange: the examples are rendered by a different spec, so letting their bullets answer for a
        // doc quoting a check over this solution would resolve a quote against a rule the self-spec never
        // rendered.
        string[] tracked =
        [
            "AGENTS.md",
            "examples/Meridian/AGENTS.md",
            "src/Zphil.LoadBearing/AGENTS.md",
            "README.md"
        ];

        // Act
        IReadOnlyList<string> cards = RuleQuotes.RenderedCards(string.Empty, tracked);

        // Assert
        cards.ShouldBe(["AGENTS.md", "src/Zphil.LoadBearing/AGENTS.md"]);
    }

    [Fact]
    public void RenderedBullets_MissingCard_Skipped()
    {
        // Arrange: a tracked path that cannot be read yields no bullets rather than throwing, so a gate
        // reports a named drift instead of dying on the first absent file.
        string[] tracked = ["examples/Ex/AGENTS.md", "examples/Ex/src/Gone/AGENTS.md"];
        string card = Card($"- `a/rule` {EmDash} the only rendered sentence.");
        Func<string, string?> reader = path => path == "examples/Ex/AGENTS.md" ? card : null;

        // Act
        IReadOnlyDictionary<string, IReadOnlyList<string>> bullets = RuleQuotes.RenderedBullets("examples/Ex", tracked, reader);

        // Assert
        bullets.ShouldHaveSingleItem()
            .Key.ShouldBe("a/rule");
    }

    private static string Card(params string[] bodyLines)
    {
        return string.Join(
            "\n",
            ["<!-- loadbearing:begin -->", .. bodyLines, "<!-- loadbearing:end -->"]);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Bullets(string ruleId, params string[] texts)
    {
        return new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { [ruleId] = texts };
    }
}
