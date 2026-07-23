using Shouldly;
using Xunit;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     Unit and negative tests for <see cref="DocProse" />. They pin the fence stripping and the
///     prose measurements, and prove that each forbidden reference and off-voice word trips the same
///     checker the documentation gates run on.
/// </summary>
public sealed class DocProseTests
{
    private const char EmDash = (char)0x2014;

    [Fact]
    public void StripFences_FencedBlock_RemovesFenceAndKeepsSurroundingProse()
    {
        // Arrange
        string[] lines = [$"before {EmDash} one", "```", $"inside {EmDash} fenced", "```", $"after {EmDash} two"];
        string text = string.Join("\n", lines);

        // Act
        string stripped = DocProse.StripFences(text);

        // Assert
        stripped.ShouldContain("before");
        stripped.ShouldContain("after");
        stripped.ShouldNotContain("inside");
        DocProse.CountEmDashes(stripped).ShouldBe(2);
    }

    [Fact]
    public void StripFences_TildeFence_Removed()
    {
        // Arrange
        string[] lines = ["keep one", "~~~", $"drop {EmDash} me", "~~~", "keep two"];
        string text = string.Join("\n", lines);

        // Act
        string stripped = DocProse.StripFences(text);

        // Assert
        stripped.ShouldContain("keep one");
        stripped.ShouldContain("keep two");
        stripped.ShouldNotContain("drop");
        DocProse.CountEmDashes(stripped).ShouldBe(0);
    }

    [Fact]
    public void StripFences_ClosingFenceLongerThanOpener_Closes()
    {
        // Arrange
        string[] lines = ["a", "```", "inside", "`````", "b"];
        string text = string.Join("\n", lines);

        // Act
        string stripped = DocProse.StripFences(text);

        // Assert
        stripped.ShouldContain("a");
        stripped.ShouldContain("b");
        stripped.ShouldNotContain("inside");
    }

    [Fact]
    public void StripFences_ShorterRunOfFenceCharacter_DoesNotClose()
    {
        // Arrange: a three-backtick line cannot close a four-backtick fence, so the block runs on to
        // the four-backtick line and everything between it and the opener is removed.
        string[] lines = ["a", "````", "inside", "```", "still inside", "````", "b"];
        string text = string.Join("\n", lines);

        // Act
        string stripped = DocProse.StripFences(text);

        // Assert
        stripped.ShouldContain("a");
        stripped.ShouldContain("b");
        stripped.ShouldNotContain("inside");
        stripped.ShouldNotContain("still inside");
    }

    [Fact]
    public void StripFences_UnclosedFence_StripsToEndOfText()
    {
        // Arrange
        string[] lines = ["keep this", "```", "dropped to the end", "also dropped"];
        string text = string.Join("\n", lines);

        // Act
        string stripped = DocProse.StripFences(text);

        // Assert
        stripped.ShouldContain("keep this");
        stripped.ShouldNotContain("dropped");
    }

    [Fact]
    public void StripFences_CrlfInput_RecognizesFences()
    {
        // Arrange
        string text = "before\r\n```\r\ninside " + EmDash + " x\r\n```\r\nafter";

        // Act
        string stripped = DocProse.StripFences(text);

        // Assert
        stripped.ShouldContain("before");
        stripped.ShouldContain("after");
        stripped.ShouldNotContain("inside");
        DocProse.CountEmDashes(stripped).ShouldBe(0);
    }

    [Fact]
    public void CountWords_WhitespaceSeparatedTokens_CountsRuns()
    {
        // Arrange
        var text = "  leading and   multiple\tspaces\nand newline ";

        // Act
        int words = DocProse.CountWords(text);

        // Assert
        words.ShouldBe(6);
    }

    [Fact]
    public void CountEmDashes_EmDashOccurrences_Counted()
    {
        // Arrange
        var withDashes = $"a{EmDash}b{EmDash}c and none here";

        // Act & Assert
        DocProse.CountEmDashes(withDashes).ShouldBe(2);
        DocProse.CountEmDashes("no dashes at all").ShouldBe(0);
    }

    [Fact]
    public void CountTics_MixedCase_CountedCaseInsensitively()
    {
        // Act & Assert
        DocProse.CountTics("This is deliberately here and intentionally there").ShouldBe(2);
        DocProse.CountTics("Deliberately capitalised and INTENTIONALLY shouted").ShouldBe(2);
        DocProse.CountTics("nothing of the sort").ShouldBe(0);
    }

    [Fact]
    public void Budget_OneEmDashInFiveHundredWords_WithinCeiling()
    {
        // Arrange: 500 words carrying exactly one em-dash, whose ceiling budget is one.
        string text = string.Join(" ", Enumerable.Repeat("word", 499)) + $" one{EmDash}dash";

        // Act
        int words = DocProse.CountWords(text);
        int emDashes = DocProse.CountEmDashes(text);
        var budget = (int)Math.Ceiling(words / 1000.0);

        // Assert
        words.ShouldBe(500);
        emDashes.ShouldBe(1);
        emDashes.ShouldBeLessThanOrEqualTo(budget);
    }

    [Fact]
    public void Budget_TwoEmDashesWhenCeilingIsOne_ExceedsBudget()
    {
        // Arrange: 500 words carrying two em-dashes, whose ceiling budget is still one.
        string text = string.Join(" ", Enumerable.Repeat("word", 498)) + $" one{EmDash}dash two{EmDash}dash";

        // Act
        int words = DocProse.CountWords(text);
        int emDashes = DocProse.CountEmDashes(text);
        var budget = (int)Math.Ceiling(words / 1000.0);

        // Assert
        words.ShouldBe(500);
        emDashes.ShouldBe(2);
        emDashes.ShouldBeGreaterThan(budget);
    }

    [Theory]
    [InlineData("Phase 12")]
    [InlineData("WP3")]
    [InlineData("WP 3")]
    [InlineData("DESIGN.md")]
    [InlineData("PLAN.md")]
    [InlineData("PLAN-ARCHIVE.md")]
    [InlineData("EXAMPLES.md")]
    [InlineData("GUIDANCE-PACK.md")]
    [InlineData("docs/notes.md")]
    public void FindForbidden_ForbiddenTokenOnSecondLine_ReportsHitWithLineNumber(string token)
    {
        // Arrange
        var text = $"first clean line\nprefix {token} suffix\nthird clean line";

        // Act
        var hits = DocProse.FindForbidden(text, DocHygieneTests.InternalReferencePatterns);

        // Assert
        hits.ShouldNotBeEmpty();
        hits.ShouldAllBe(hit => hit.StartsWith("2: "));
    }

    [Theory]
    [InlineData("a phase 9 lowercase note")]
    [InlineData("phase two, without a digit")]
    [InlineData("see PLAN.mdx for details")]
    [InlineData("the EXAMPLES.mdown file")]
    public void FindForbidden_BenignLookalike_ReportsNoHit(string text)
    {
        // Act
        var hits = DocProse.FindForbidden(text, DocHygieneTests.InternalReferencePatterns);

        // Assert
        hits.ShouldBeEmpty();
    }

    [Fact]
    public void FindForbidden_OffVoiceWord_ReportsHitWithLineNumber()
    {
        // Arrange
        var text = "The documentation reads clearly.\nThe codebase is messy today.";

        // Act
        var hits = DocProse.FindForbidden(text, DocHygieneTests.HouseVoicePatterns);

        // Assert
        hits.ShouldHaveSingleItem().ShouldBe("2: messy");
    }

    [Theory]
    [InlineData("the files were arranged messily")]
    [InlineData("open the messyroom door")]
    public void FindForbidden_OffVoiceLookalike_ReportsNoHit(string text)
    {
        // Act
        var hits = DocProse.FindForbidden(text, DocHygieneTests.HouseVoicePatterns);

        // Assert
        hits.ShouldBeEmpty();
    }
}