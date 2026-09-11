using Shouldly;
using Xunit;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     Unit and negative tests for <see cref="GrandfatheredCounts" />. They pin the four fenced count
///     shapes — including the sub-line that takes its rule from the header above it in its own fence —
///     the baseline lookup, the classifier that decides whether a quoted count still agrees with the
///     committed <c>entries</c>, and the prose machinery the registered templates are composed and swept
///     with, so the gate and these tests exercise the same code path.
/// </summary>
public sealed class GrandfatheredCountsTests
{
    private const char EmDash = (char)0x2014;

    [Fact]
    public void Extract_StatusRatchetLine_CapturedWithRuleAndBothCounters()
    {
        // Arrange
        string doc = string.Join(
            "\n",
            "```text",
            $"pass time/inject-clock (migrate) {EmDash} 7 grandfathered remaining, 0 new, 2 fixed awaiting acceptance",
            "```");

        // Act
        IReadOnlyList<GrandfatheredCount> counts = GrandfatheredCounts.Extract("d.md", doc);

        // Assert
        GrandfatheredCount count = counts.ShouldHaveSingleItem();
        count.Doc.ShouldBe("d.md");
        count.DocLine.ShouldBe(2);
        count.RuleId.ShouldBe("time/inject-clock");
        count.Count.ShouldBe(7);
        count.Stale.ShouldBe(2);
    }

    [Fact]
    public void Extract_StatusRatchetLineCarryingTheMeasureClauses_StillCapturesBothCounters()
    {
        // Arrange: the same line with every clause the measure added — the site total behind the remaining
        // pairs, and the two states a write clears. None of them moves the entry count, so all three have to
        // be optional in the pattern: unrecognized, the line stops matching and the gate holds nothing at
        // all, which reads as green rather than as a failure.
        string doc = string.Join(
            "\n",
            "```text",
            $"pass time/inject-clock (migrate) {EmDash} 7 grandfathered remaining (19 sites), 0 new, "
            + "2 fixed awaiting acceptance, 1 shrunk, 3 uncounted",
            "```");

        // Act
        IReadOnlyList<GrandfatheredCount> counts = GrandfatheredCounts.Extract("d.md", doc);

        // Assert
        GrandfatheredCount count = counts.ShouldHaveSingleItem();
        count.RuleId.ShouldBe("time/inject-clock");
        count.Count.ShouldBe(7);
        count.Stale.ShouldBe(2);
    }

    [Theory]
    [InlineData("data-access/no-inline-sql: captured 12 grandfathered violations.", "data-access/no-inline-sql", 12)]
    [InlineData("clearance/engine/containment: captured 1 grandfathered violation.", "clearance/engine/containment", 1)]
    public void Extract_BaselineCaptureLine_CapturedEitherPlurality(string line, string ruleId, int expected)
    {
        // Arrange
        string doc = string.Join("\n", "```text", line, "```");

        // Act
        IReadOnlyList<GrandfatheredCount> counts = GrandfatheredCounts.Extract("d.md", doc);

        // Assert: `baseline --init` writes exactly what it captured, so the line carries no stale count.
        GrandfatheredCount count = counts.ShouldHaveSingleItem();
        count.RuleId.ShouldBe(ruleId);
        count.Count.ShouldBe(expected);
        count.Stale.ShouldBeNull();
    }

    [Fact]
    public void Extract_BurndownSummary_CapturedAsTheRootTotal()
    {
        // Arrange
        string doc = string.Join(
            "\n",
            "```text",
            "Checked 8 rules: 7 passed, 0 failed, 1 skipped. Burndown: 33 grandfathered remaining, 0 fixed awaiting acceptance.",
            "```");

        // Act
        IReadOnlyList<GrandfatheredCount> counts = GrandfatheredCounts.Extract("d.md", doc);

        // Assert
        GrandfatheredCount count = counts.ShouldHaveSingleItem();
        count.RuleId.ShouldBe(GrandfatheredCounts.RootTotal);
        count.Count.ShouldBe(33);
        count.Stale.ShouldBe(0);
    }

    [Fact]
    public void Extract_BurndownSummaryCarryingSitesAndTheRecordNudge_StillCapturesBothCounters()
    {
        // Arrange: the summary's own two clauses. The uncounted one ends in the nudge to record the counts,
        // which moves the sentence's full stop past it — so the pattern has to reach the period through the
        // advice rather than expecting it after "acceptance".
        string doc = string.Join(
            "\n",
            "```text",
            "Checked 8 rules: 7 passed, 0 failed, 1 skipped. Burndown: 33 grandfathered remaining (53 sites), "
            + "0 fixed awaiting acceptance, 33 uncounted; run 'loadbearing baseline --accept-reductions' to record site counts.",
            "```");

        // Act
        IReadOnlyList<GrandfatheredCount> counts = GrandfatheredCounts.Extract("d.md", doc);

        // Assert
        GrandfatheredCount count = counts.ShouldHaveSingleItem();
        count.RuleId.ShouldBe(GrandfatheredCounts.RootTotal);
        count.Count.ShouldBe(33);
        count.Stale.ShouldBe(0);
    }

    [Fact]
    public void Extract_SubLineUnderAReportHeader_TakesThatRule()
    {
        // Arrange: the sub-line names no rule, which is the whole reason the scanner works in blocks.
        string doc = string.Join(
            "\n",
            "```text",
            $"FAIL data-access/no-inline-sql {EmDash} Types in `Web.*` must not reference `SqlConnection`.",
            "  src/Web/Controllers/BookingsController.cs:77 " + EmDash + " a violation",
            "  grandfathered: 12 (baselined; run 'loadbearing status' for burndown)",
            "```");

        // Act
        IReadOnlyList<GrandfatheredCount> counts = GrandfatheredCounts.Extract("d.md", doc);

        // Assert
        GrandfatheredCount count = counts.ShouldHaveSingleItem();
        count.DocLine.ShouldBe(4);
        count.RuleId.ShouldBe("data-access/no-inline-sql");
        count.Count.ShouldBe(12);
    }

    [Fact]
    public void Extract_SubLineUnderAJsonRuleId_TakesThatRule()
    {
        // Arrange: `check --json` declares the rule as a field and nests the count in a baseline object.
        string doc = string.Join(
            "\n",
            "```json",
            "{",
            "  \"id\": \"mcp/env-through-seam\",",
            "  \"baseline\": {",
            "    \"grandfathered\": 2,",
            "    \"stale\": 0",
            "  }",
            "}",
            "```");

        // Act
        IReadOnlyList<GrandfatheredCount> counts = GrandfatheredCounts.Extract("d.md", doc);

        // Assert
        GrandfatheredCount count = counts.ShouldHaveSingleItem();
        count.DocLine.ShouldBe(5);
        count.RuleId.ShouldBe("mcp/env-through-seam");
        count.Count.ShouldBe(2);
    }

    [Fact]
    public void Extract_RuleIdDoesNotLeakOutOfItsFence()
    {
        // Arrange: a rule declared in one block must never answer for a sub-line in the next, or a count
        // would be checked against a baseline it was never printed from.
        string doc = string.Join(
            "\n",
            "```text",
            $"pass a/rule {EmDash} the first block declares a rule.",
            "```",
            "prose between blocks",
            "```text",
            "  grandfathered: 5",
            "```");

        // Act
        IReadOnlyList<GrandfatheredCount> counts = GrandfatheredCounts.Extract("d.md", doc);

        // Assert
        counts.ShouldHaveSingleItem()
            .RuleId.ShouldBeEmpty();
    }

    [Fact]
    public void Extract_CountsOutsideAFence_Ignored()
    {
        // Arrange: prose restates the captured counts and is held by the registered templates instead.
        string doc = string.Join(
            "\n",
            "The twelve grandfathered sites stay quiet, reported as `grandfathered: 12`.",
            "data-access/no-inline-sql: captured 12 grandfathered violations.");

        // Act
        IReadOnlyList<GrandfatheredCount> counts = GrandfatheredCounts.Extract("d.md", doc);

        // Assert
        counts.ShouldBeEmpty();
    }

    [Fact]
    public void EntryCount_RuleSection_IsTheEntriesLength()
    {
        // Act & Assert
        GrandfatheredCounts.EntryCount(Baseline(("a/rule", 3)), "a/rule")
            .ShouldBe(3);
    }

    [Fact]
    public void EntryCount_RuleTheFileDoesNotHold_Null()
    {
        // Act & Assert
        GrandfatheredCounts.EntryCount(Baseline(("a/rule", 3)), "b/rule")
            .ShouldBeNull();
    }

    [Fact]
    public void EntryCount_RootTotal_SumsEverySectionInTheFile()
    {
        // Act & Assert
        GrandfatheredCounts.EntryCount(Baseline(("a/rule", 3), ("b/rule", 4)), GrandfatheredCounts.RootTotal)
            .ShouldBe(7);
    }

    [Fact]
    public void Classify_BaselineHoldsTheQuotedCount_Matched()
    {
        // Arrange
        GrandfatheredCount count = new("d.md", 12, "time/inject-clock", 7, null);
        Func<string, string?> read = Reader(("examples/Meridian/arch/baselines/time/inject-clock.json", Baseline(("time/inject-clock", 7))));

        // Act
        GrandfatheredCounts.CountResult result = GrandfatheredCounts.Classify(count, "examples/Meridian", [], read);

        // Assert
        result.Bucket.ShouldBe(GrandfatheredCounts.CountBucket.Matched);
        result.Failure.ShouldBeNull();
    }

    [Fact]
    public void Classify_QuotedStaleAccountsForTheRemainingEntries_Matched()
    {
        // Arrange: entries = grandfathered + stale, exactly as the checker computes them, so a line that
        // carries the stale count is checked whole rather than only where the baseline is fully matched.
        GrandfatheredCount count = new("d.md", 12, "time/inject-clock", 5, 2);
        Func<string, string?> read = Reader(("examples/Meridian/arch/baselines/time/inject-clock.json", Baseline(("time/inject-clock", 7))));

        // Act
        GrandfatheredCounts.CountResult result = GrandfatheredCounts.Classify(count, "examples/Meridian", [], read);

        // Assert
        result.Bucket.ShouldBe(GrandfatheredCounts.CountBucket.Matched);
    }

    [Fact]
    public void Classify_ReBaselinedRule_Drifted()
    {
        // Arrange: the re-baseline this gate exists to catch — the walkthrough still quotes the old count.
        GrandfatheredCount count = new("d.md", 12, "time/inject-clock", 8, null);
        Func<string, string?> read = Reader(("examples/Meridian/arch/baselines/time/inject-clock.json", Baseline(("time/inject-clock", 7))));

        // Act
        GrandfatheredCounts.CountResult result = GrandfatheredCounts.Classify(count, "examples/Meridian", [], read);

        // Assert: the failure names the doc, the line, the rule, the quoted count and what is committed.
        result.Bucket.ShouldBe(GrandfatheredCounts.CountBucket.Drifted);
        result.Failure!.ShouldContain("d.md:12");
        result.Failure!.ShouldContain("time/inject-clock");
        result.Failure!.ShouldContain("8 grandfathered");
        result.Failure!.ShouldContain("holds 7");
    }

    [Fact]
    public void Classify_RenamedBaseline_NoBaseline()
    {
        // Arrange
        GrandfatheredCount count = new("d.md", 12, "time/inject-clock", 7, null);
        Func<string, string?> read = _ => null;

        // Act
        GrandfatheredCounts.CountResult result = GrandfatheredCounts.Classify(count, "examples/Meridian", [], read);

        // Assert
        result.Bucket.ShouldBe(GrandfatheredCounts.CountBucket.NoBaseline);
        result.Failure!.ShouldContain("examples/Meridian/arch/baselines/time/inject-clock.json");
    }

    [Fact]
    public void Classify_SubLineWithNoRuleAboveIt_NoBaseline()
    {
        // Arrange: an unattributable count is reported, never silently skipped.
        GrandfatheredCount count = new("d.md", 12, string.Empty, 5, null);
        Func<string, string?> read = _ => null;

        // Act
        GrandfatheredCounts.CountResult result = GrandfatheredCounts.Classify(count, "examples/Meridian", [], read);

        // Assert
        result.Bucket.ShouldBe(GrandfatheredCounts.CountBucket.NoBaseline);
        result.Failure!.ShouldContain("no rule declared above it in its fence");
    }

    [Fact]
    public void Classify_RootTotal_SumsEveryBaselineUnderTheRoot()
    {
        // Arrange
        GrandfatheredCount count = new("d.md", 12, GrandfatheredCounts.RootTotal, 10, 0);
        string[] tracked =
        [
            "examples/Meridian/arch/baselines/a/rule.json",
            "examples/Meridian/arch/baselines/scoped/b/rule.json",
            "examples/Meridian.Quoting/arch/baselines/c/rule.json"
        ];
        Func<string, string?> read = Reader(
            ("examples/Meridian/arch/baselines/a/rule.json", Baseline(("a/rule", 4))),
            ("examples/Meridian/arch/baselines/scoped/b/rule.json", Baseline(("scoped/b/rule", 6))),
            ("examples/Meridian.Quoting/arch/baselines/c/rule.json", Baseline(("c/rule", 99))));

        // Act
        GrandfatheredCounts.CountResult result = GrandfatheredCounts.Classify(count, "examples/Meridian", tracked, read);

        // Assert: the sibling example shares the root as a string but not as a directory, so it is out.
        result.Bucket.ShouldBe(GrandfatheredCounts.CountBucket.Matched);
    }

    [Theory]
    [InlineData("", "time/inject-clock", "arch/baselines/time/inject-clock.json")]
    [InlineData("examples/Meridian", "clearance/engine/containment", "examples/Meridian/arch/baselines/clearance/engine/containment.json")]
    public void BaselinePath_RuleId_NestsOnItsSlashes(string exampleRoot, string ruleId, string expected)
    {
        // Act & Assert
        GrandfatheredCounts.BaselinePath(exampleRoot, ruleId)
            .ShouldBe(expected);
    }

    [Fact]
    public void BaselineFiles_SelfRoot_ExcludesTheExamplesAndTheFixtures()
    {
        // Arrange: this repository's own burndown totals its own baselines, and both the examples and the
        // fixture solutions carry directories of the same shape further down the tree.
        string[] tracked =
        [
            "arch/baselines/mcp/env-through-seam.json",
            "arch/baselines/roslyn/msbuild-bootstrap/containment.json",
            "examples/Meridian/arch/baselines/time/inject-clock.json",
            "tests/Zphil.LoadBearing.Tests/Fixtures/TestSolutions/MyApp/arch/baselines/legacy/billing/containment.json",
            "arch/Zphil.LoadBearing.ArchSpec/LoadBearingArchSpec.cs"
        ];

        // Act
        IReadOnlyList<string> files = GrandfatheredCounts.BaselineFiles(string.Empty, tracked);

        // Assert
        files.ShouldBe(["arch/baselines/mcp/env-through-seam.json", "arch/baselines/roslyn/msbuild-bootstrap/containment.json"]);
    }

    [Theory]
    [InlineData(0, "zero")]
    [InlineData(1, "one")]
    [InlineData(13, "thirteen")]
    [InlineData(20, "twenty")]
    [InlineData(33, "thirty-three")]
    public void Spell_WordStyle_ReadsAsTheProseWritesIt(int count, string expected)
    {
        // Act & Assert
        GrandfatheredCounts.Spell(count, GrandfatheredCounts.CountStyle.Word)
            .ShouldBe(expected);
    }

    [Fact]
    public void Spell_NumeralAndTitleWordStyles_ReadAsTheirSurfacesWriteThem()
    {
        // Act & Assert: the tool prints digits, and a sentence that opens on the number capitalizes it.
        GrandfatheredCounts.Spell(12, GrandfatheredCounts.CountStyle.Numeral)
            .ShouldBe("12");
        GrandfatheredCounts.Spell(33, GrandfatheredCounts.CountStyle.TitleWord)
            .ShouldBe("Thirty-three");
    }

    [Fact]
    public void Spell_CountBeyondTheWordTable_Throws()
    {
        // Act & Assert: an unspellable number would compose a fragment nothing contains, and a gate that
        // can only fail for the wrong reason is worse than one that says it cannot answer.
        Should.Throw<ArgumentOutOfRangeException>(() => GrandfatheredCounts.Spell(100, GrandfatheredCounts.CountStyle.Word));
    }

    [Fact]
    public void Compose_TemplateAndCounts_FillsEveryPlaceholder()
    {
        // Act
        string fragment = GrandfatheredCounts.Compose(
            "holding {0} inline-SQL references and {1} inbound reach",
            [12, 1],
            GrandfatheredCounts.CountStyle.Word);

        // Assert
        fragment.ShouldBe("holding twelve inline-SQL references and one inbound reach");
    }

    [Fact]
    public void Collapse_WrappedProse_IsContiguousAndKeepsItsLineNumbers()
    {
        // Arrange: a registered fragment routinely spans a line break, which is what collapsing is for.
        string doc = string.Join("\n", "# Title", "", "goes red while its thirteen grandfathered", "sites stay quiet.");

        // Act
        GrandfatheredCounts.CollapsedText collapsed = GrandfatheredCounts.Collapse(doc);

        // Assert
        collapsed.Text.ShouldContain("its thirteen grandfathered sites stay quiet.");
        collapsed.Span("thirteen grandfathered sites")
            .ShouldBe((3, 4));
    }

    [Fact]
    public void Collapse_FragmentTheDocDoesNotCarry_HasNoSpan()
    {
        // Act & Assert
        GrandfatheredCounts.Collapse("some prose")
            .Span("other prose")
            .ShouldBeNull();
    }

    [Fact]
    public void ProseMentions_CountNearTheWord_Swept()
    {
        // Arrange
        var doc = "the generated baseline carries four entries. Because they are grandfathered, check exits 0";

        // Act
        IReadOnlyList<GrandfatheredCounts.ProseMention> mentions = GrandfatheredCounts.ProseMentions("d.md", doc);

        // Assert
        mentions.ShouldHaveSingleItem()
            .DocLine.ShouldBe(1);
    }

    [Fact]
    public void ProseMentions_CountFarFromTheWord_NotSwept()
    {
        // Arrange: a number elsewhere in a long line is discussing something else — here the "one test per
        // rule" of a CI step, sentences away from the word.
        var doc = "a Migrate rule's grandfathered sites keep their test green while the ratchet holds, and CI runs it as a step of its own, one line per rule ID.";

        // Act
        IReadOnlyList<GrandfatheredCounts.ProseMention> mentions = GrandfatheredCounts.ProseMentions("d.md", doc);

        // Assert
        mentions.ShouldBeEmpty();
    }

    [Fact]
    public void ProseMentions_CountsInsideAFence_NotSwept()
    {
        // Arrange: the fenced shapes are held by the extractor, so sweeping them here would double-report.
        string doc = string.Join(
            "\n",
            "```text",
            "  grandfathered: 12 (baselined; run 'loadbearing status' for burndown)",
            "```",
            "and its twelve grandfathered sites stay quiet");

        // Act
        IReadOnlyList<GrandfatheredCounts.ProseMention> mentions = GrandfatheredCounts.ProseMentions("d.md", doc);

        // Assert
        mentions.ShouldHaveSingleItem()
            .DocLine.ShouldBe(4);
    }

    [Fact]
    public void ProseMentions_WordWithNoCountNearIt_NotSwept()
    {
        // Arrange
        var doc = "That is grandfathered debt, not house style.";

        // Act
        IReadOnlyList<GrandfatheredCounts.ProseMention> mentions = GrandfatheredCounts.ProseMentions("d.md", doc);

        // Assert
        mentions.ShouldBeEmpty();
    }

    private static string Baseline(params (string RuleId, int Entries)[] sections)
    {
        IEnumerable<string> rules = sections
            .Select(section =>
            {
                string entries = string.Join(", ", Enumerable.Repeat("{ \"subject\": \"M:X.Y\" }", section.Entries));
                return $"    \"{section.RuleId}\": {{ \"entries\": [{entries}] }}";
            });

        return $"{{\n  \"schemaVersion\": 1,\n  \"rules\": {{\n{string.Join(",\n", rules)}\n  }}\n}}";
    }

    private static Func<string, string?> Reader(params (string Path, string Text)[] files)
    {
        return requested => files
            .Where(file => file.Path == requested)
            .Select(file => file.Text)
            .FirstOrDefault();
    }
}
