using Shouldly;
using Xunit;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     The fast <c>baseline</c> paths that need no workspace: mode + companion validation runs before
///     solution discovery, so every malformed invocation exits 2 with its pinned message even when the
///     solution argument is nonsense. Covers the three-way mode exclusivity (<c>--init</c> /
///     <c>--accept-reductions</c> / <c>--add</c>), the "companions only with --add" guard, and each
///     <c>--add</c> precondition: a required <c>--rule</c>, a non-blank single-line <c>--because</c>, and
///     exactly one entry form (an edge <c>--source</c> + <c>--target</c>, or a shape <c>--subject</c>).
/// </summary>
public sealed class BaselineCommandTests
{
    [Fact]
    public async Task Baseline_NoMode_ValidatesBeforeSolutionDiscovery()
    {
        // A nonexistent solution: if mode validation did not run first this would be "solution not found".
        CliResult result = await CliRunner.InvokeAsync("baseline", "does-not-exist.sln");

        result.Exit.ShouldBe(2);
        result.Err.ShouldContain("Specify exactly one of --init, --accept-reductions, or --add.");
        result.Err.ShouldNotContain("does-not-exist");
    }

    [Fact]
    public async Task Baseline_BothModes_ExitsTwo()
    {
        CliResult result = await CliRunner.InvokeAsync("baseline", "--init", "--accept-reductions");

        result.Exit.ShouldBe(2);
        result.Err.ShouldContain("Specify exactly one of --init, --accept-reductions, or --add.");
    }

    [Fact]
    public async Task Baseline_AllThreeModes_ValidatesBeforeSolutionDiscovery()
    {
        CliResult result = await CliRunner.InvokeAsync(
            "baseline", "does-not-exist.sln", "--init", "--accept-reductions", "--add");

        result.Exit.ShouldBe(2);
        result.Err.ShouldContain("Specify exactly one of --init, --accept-reductions, or --add.");
        result.Err.ShouldNotContain("does-not-exist");
    }

    [Fact]
    public async Task Baseline_AddCompanionsWithoutAdd_ExitsTwo()
    {
        CliResult result = await CliRunner.InvokeAsync("baseline", "does-not-exist.sln", "--init", "--rule", "some/rule");

        result.Exit.ShouldBe(2);
        result.Err.ShouldContain("--rule, --because, --source, --target, and --subject apply only with --add.");
    }

    [Fact]
    public async Task Baseline_AddWithoutRule_ExitsTwo()
    {
        CliResult result = await CliRunner.InvokeAsync(
            "baseline", "does-not-exist.sln", "--add", "--because", "b", "--subject", "S");

        result.Exit.ShouldBe(2);
        result.Err.ShouldContain("--add requires --rule <id>.");
    }

    [Fact]
    public async Task Baseline_AddWithBlankOrMultilineBecause_ExitsTwo()
    {
        // Missing, whitespace-only, and multi-line --because all refuse with the one pinned message.
        CliResult missing = await CliRunner.InvokeAsync(
            "baseline", "does-not-exist.sln", "--add", "--rule", "r", "--subject", "S");
        CliResult blank = await CliRunner.InvokeAsync(
            "baseline", "does-not-exist.sln", "--add", "--rule", "r", "--subject", "S", "--because", "   ");
        CliResult multiline = await CliRunner.InvokeAsync(
            "baseline", "does-not-exist.sln", "--add", "--rule", "r", "--subject", "S", "--because", "a\nb");

        const string expected = "--add requires a non-blank, single-line --because.";
        missing.Exit.ShouldBe(2);
        missing.Err.ShouldContain(expected);
        blank.Exit.ShouldBe(2);
        blank.Err.ShouldContain(expected);
        multiline.Exit.ShouldBe(2);
        multiline.Err.ShouldContain(expected);
    }

    [Fact]
    public async Task Baseline_AddWithoutExactlyOneEntryForm_ExitsTwo()
    {
        // A half-edge (--source, no --target), both forms mixed (--source + --subject), and neither form each refuse.
        CliResult halfEdge = await CliRunner.InvokeAsync(
            "baseline", "does-not-exist.sln", "--add", "--rule", "r", "--because", "b", "--source", "X");
        CliResult mixed = await CliRunner.InvokeAsync(
            "baseline", "does-not-exist.sln", "--add", "--rule", "r", "--because", "b", "--subject", "S", "--source", "X");
        CliResult neither = await CliRunner.InvokeAsync(
            "baseline", "does-not-exist.sln", "--add", "--rule", "r", "--because", "b");

        const string expected =
            "--add requires exactly one entry form: --source with --target (an edge), or --subject (a shape).";
        halfEdge.Exit.ShouldBe(2);
        halfEdge.Err.ShouldContain(expected);
        mixed.Exit.ShouldBe(2);
        mixed.Err.ShouldContain(expected);
        neither.Exit.ShouldBe(2);
        neither.Err.ShouldContain(expected);
    }
}