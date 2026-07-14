using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Mcp.Pipeline;

namespace Zphil.LoadBearing.Tests.Mcp;

/// <summary>
///     The response cap (<see cref="ResponseTruncator" />): the token-budget → char-cap conversion and the
///     truncate-at-last-newline behavior. The donor's tool-specific narrowing-hint rows are dropped — the
///     <c>arch_*</c> tools have no per-tool hint, so the footer always ends the same way.
/// </summary>
public sealed class ResponseTruncatorTests
{
    [Fact]
    public void ComputeMaxChars_NullValue_ReturnsDefault()
    {
        ResponseTruncator.ComputeMaxChars(null).ShouldBe(25_000);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-number")]
    [InlineData("0")]
    [InlineData("-100")]
    public void ComputeMaxChars_BlankUnparseableOrNonPositive_ReturnsDefault(string value)
    {
        ResponseTruncator.ComputeMaxChars(value).ShouldBe(25_000);
    }

    [Theory]
    [InlineData("1000", 2_500)]
    [InlineData("4000", 10_000)]
    public void ComputeMaxChars_PositiveTokenBudget_ReturnsTokensTimesCharsPerToken(string value, int expected)
    {
        ResponseTruncator.ComputeMaxChars(value).ShouldBe(expected);
    }

    [Fact]
    public void TruncateIfNeeded_TextWithinLimit_ReturnsUnchanged()
    {
        const string text = "short output";

        ResponseTruncator.TruncateIfNeeded(text, "arch_check", 100).ShouldBe(text);
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
    public void TruncateIfNeeded_TextExceedsLimit_FooterReportsSizeAndOmittedCountAndNoHint()
    {
        string text = new('x', 50);

        string result = ResponseTruncator.TruncateIfNeeded(text, "arch_check", 20);

        result.ShouldContain("--- RESPONSE TRUNCATED ---");
        result.ShouldContain("Output was 50 characters, limit is 20");
        result.ShouldContain("30 characters omitted");
        // No per-tool narrowing hint — the footer ends here.
        result.ShouldEndWith("The results above are incomplete.");
    }
}