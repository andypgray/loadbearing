using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Mcp.Pipeline;

namespace Zphil.LoadBearing.Tests.Mcp;

/// <summary>
///     The response cap (<see cref="ResponseTruncator" />): the token-budget → char-cap conversion, the
///     truncate-at-last-newline behavior, and the per-tool narrowing hint the footer carries. Truncation is
///     the backstop, not the answer — the tools that can narrow themselves say so here, and the ones with no
///     knob deliberately say nothing, because a hint naming nothing is noise at the moment a reader is
///     looking for something to do.
/// </summary>
public sealed class ResponseTruncatorTests
{
    private const string GraphHint =
        "Narrow the subject rather than read half a survey: the grain ladder is already exhausted, so "
        + "projects: \"<name globs>\" surveys part of the solution and is the knob left. On the CLI, "
        + "loadbearing graph --projects <globs> --json, or redirect loadbearing graph --json to a file "
        + "and slice it there.";

    private const string CheckHint =
        "Narrow the call rather than read half a report: rules: \"<rule-id globs>\" checks a subset, and "
        + "arch_explain returns one rule whole. For the report entire, redirect "
        + "loadbearing check --json to a file and slice it there.";

    [Fact]
    public void ComputeMaxChars_NullValue_ReturnsDefault()
    {
        ResponseTruncator.ComputeMaxChars(null)
            .ShouldBe(62_500);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-number")]
    [InlineData("0")]
    [InlineData("-100")]
    public void ComputeMaxChars_BlankUnparseableOrNonPositive_ReturnsDefault(string value)
    {
        ResponseTruncator.ComputeMaxChars(value)
            .ShouldBe(62_500);
    }

    [Theory]
    [InlineData("1000", 2_500)]
    [InlineData("4000", 10_000)]
    public void ComputeMaxChars_PositiveTokenBudget_ReturnsTokensTimesCharsPerToken(string value, int expected)
    {
        ResponseTruncator.ComputeMaxChars(value)
            .ShouldBe(expected);
    }

    [Fact]
    public void TruncateIfNeeded_TextWithinLimit_ReturnsUnchanged()
    {
        const string text = "short output";

        ResponseTruncator.TruncateIfNeeded(text, "arch_check", 100)
            .ShouldBe(text);
    }

    [Fact]
    public void TruncateIfNeeded_TextExceedsLimit_CutsAtLastNewlineBeforeCap()
    {
        // A newline sits at index 5 and index 11; the cap falls at 12.
        const string text = "line1\nline2\nline3-and-a-long-tail-past-the-cap";

        string result = ResponseTruncator.TruncateIfNeeded(text, null, 12);

        result.ShouldStartWith("line1\nline2\n\n--- RESPONSE TRUNCATED ---");
    }

    [Fact]
    public void TruncateIfNeeded_NoNewlineBeforeCap_CutsAtCap()
    {
        const string text = "abcdefghijklmnopqrstuvwxyz";

        string result = ResponseTruncator.TruncateIfNeeded(text, null, 8);

        result.ShouldStartWith("abcdefgh\n\n--- RESPONSE TRUNCATED ---");
    }

    [Fact]
    public void TruncateIfNeeded_CutFallsBetweenSurrogatePair_StepsBackSoNoLoneSurrogateRemains()
    {
        const int maxChars = 10;
        // Nine ASCII chars, then "😀" (U+1F600 = two UTF-16 units): its HIGH surrogate lands at index 9 and
        // its LOW surrogate at index 10, so a naive cut at the cap keeps a lone high surrogate. Trailing
        // filler (no newline) forces the no-newline cut-at-cap path.
        string text = new string('a', maxChars - 1) + "\U0001F600" + new string('b', maxChars);

        string result = ResponseTruncator.TruncateIfNeeded(text, null, maxChars);

        // The kept prefix stops before the split emoji — nine 'a's, no dangling surrogate.
        string kept = result[..result.IndexOf('\n')];
        kept.ShouldBe(new string('a', maxChars - 1));
        char.IsHighSurrogate(kept[^1])
            .ShouldBeFalse();
    }

    [Fact]
    public void TruncateIfNeeded_TextExceedsLimit_FooterReportsSizeAndOmittedCountAndTheToolHint()
    {
        string text = new('x', 50);

        string result = ResponseTruncator.TruncateIfNeeded(text, "arch_check", 20);

        result.ShouldContain("--- RESPONSE TRUNCATED ---");
        result.ShouldContain("Output was 50 characters, limit is 20");
        result.ShouldContain("30 characters omitted");
        result.ShouldContain("The results above are incomplete.");
        // The narrowing hint is the last thing a reader sees, because it is the only actionable line here.
        result.ShouldEndWith(CheckHint);
    }

    [Fact]
    public void TruncateIfNeeded_GraphTool_FooterNamesBothKnobsAndTheirCliTwins()
    {
        string result = ResponseTruncator.TruncateIfNeeded(new string('x', 50), "arch_graph", 20);

        result.ShouldEndWith($"The results above are incomplete.\n{GraphHint}");
    }

    [Fact]
    public void TruncateIfNeeded_CheckTool_FooterNamesTheRulesKnobAndTheSingleRuleTool()
    {
        string result = ResponseTruncator.TruncateIfNeeded(new string('x', 50), "arch_check", 20);

        result.ShouldEndWith($"The results above are incomplete.\n{CheckHint}");
    }

    [Theory]
    [InlineData("arch_status")]
    [InlineData("arch_explain")]
    [InlineData("arch_context")]
    public void TruncateIfNeeded_ToolWithNoNarrowingKnob_FooterEndsWithoutAHint(string toolName)
    {
        // Nothing to name: these three take no filter, so the footer stops at the incompleteness itself.
        string result = ResponseTruncator.TruncateIfNeeded(new string('x', 50), toolName, 20);

        result.ShouldEndWith("The results above are incomplete.");
    }

    [Fact]
    public void TruncateIfNeeded_UnknownToolName_FooterEndsWithoutAHint()
    {
        string result = ResponseTruncator.TruncateIfNeeded(new string('x', 50), "some_other_tool", 20);

        result.ShouldEndWith("The results above are incomplete.");
    }
}
