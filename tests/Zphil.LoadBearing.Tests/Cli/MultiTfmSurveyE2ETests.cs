using System.Text.Json;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     The survey's target-framework pair end to end, over the <c>MultiTfm</c> fixture:
///     <c>Roslyn.MultiTargetFrameworkTests</c> pins the model and <c>Extraction.GraphSummarizerTests</c> the
///     summary; these pin the two documents a reader actually holds. A project that compiles four times and
///     says so nowhere was the whole defect — an agent deriving a spec from the survey could not see that
///     the noun it was about to anchor a rule on is one compilation of several.
/// </summary>
/// <remarks>
///     Targeted assertions and no golden, deliberately. Both keys ride the project row rather than a
///     top-level array, so what is asserted is that they are on the right row, absent from the wrong one,
///     and still there at both coarser grains — every part of the claim, none of the document's spelling.
/// </remarks>
[Collection("Serial")]
public sealed class MultiTfmSurveyE2ETests
{
    private const string Core = "MultiTfm.Core";
    private const string Web = "MultiTfm.Web";

    [Fact]
    public async Task Graph_MultiTargetedProject_NamesItsFrameworksAndTheWinningOne()
    {
        // Arrange
        using var fixture = new TempFixtureWorkspace("TestSolutions/MultiTfm", "MultiTfm.sln");

        // Act
        CliResult result = await CliRunner.InvokeAsync("graph", fixture.SolutionPath, "--json");

        // Assert — the pair a reader needs before writing arch.Project("MultiTfm.Core"): how many
        // compilations that noun spans, and which one a rule about a type they share is measured against.
        // net10.0 wins on extraction's ordinal order, not on the csproj's, which declares netstandard2.0
        // first precisely so this pin means something.
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        JsonElement core = Project(document, Core);

        Frameworks(core)
            .ShouldBe(["net10.0", "netstandard2.0"]);
        core.GetProperty("factsFollow")
            .GetString()
            .ShouldBe("net10.0");
    }

    [Fact]
    public async Task Graph_SingleTargetedProject_OmitsBothKeysEntirely()
    {
        // Arrange
        using var fixture = new TempFixtureWorkspace("TestSolutions/MultiTfm", "MultiTfm.sln");

        // Act
        CliResult result = await CliRunner.InvokeAsync("graph", fixture.SolutionPath, "--json");

        // Assert — the omit-when-empty rule, which is what keeps every survey of an ordinary solution the
        // document it was before these keys existed. Asserted on the contrast project inside the same
        // document as the one above, so the two shapes cannot drift apart.
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        JsonElement web = Project(document, Web);

        web.TryGetProperty("targetFrameworks", out _)
            .ShouldBeFalse();
        web.TryGetProperty("factsFollow", out _)
            .ShouldBeFalse();
    }

    [Theory]
    [InlineData("--overview")]
    [InlineData("--skeleton")]
    public async Task Graph_CoarserGrain_KeepsBothKeysOnTheProjectRow(string grain)
    {
        // Arrange
        using var fixture = new TempFixtureWorkspace("TestSolutions/MultiTfm", "MultiTfm.sln");

        // Act
        CliResult result = await CliRunner.InvokeAsync("graph", fixture.SolutionPath, "--json", grain);

        // Assert — the pair rides the row, so it survives every rung the roster does. That is the rung a
        // reader most needs it at: a coarser survey is the one where a project noun is all they have left,
        // and "this noun is one compilation of two" is exactly what that noun does not say by itself.
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        JsonElement core = Project(document, Core);

        Frameworks(core)
            .ShouldBe(["net10.0", "netstandard2.0"]);
        core.GetProperty("factsFollow")
            .GetString()
            .ShouldBe("net10.0");
    }

    [Fact]
    public async Task Graph_HumanSurvey_QualifiesOnlyTheMultiTargetedProjectsLine()
    {
        // Arrange
        using var fixture = new TempFixtureWorkspace("TestSolutions/MultiTfm", "MultiTfm.sln");

        // Act
        CliResult result = await CliRunner.InvokeAsync("graph", fixture.SolutionPath);

        // Assert — the same two facts a terminal reader gets, so nobody is sent to --json for them, and the
        // single-framework line beside it unannotated: only the remarkable case is marked, which is what
        // leaves the one line worth reading easy to find.
        result.ShouldSucceed();
        string output = result.Out.NormalizedLines();

        output.ShouldContain(
            $"  {Core} — 2 types; targets net10.0, netstandard2.0 (shared types from net10.0); references: (none)");
        output.ShouldContain($"  {Web} — 1 type; references: {Core}");
    }

    private static IReadOnlyList<string?> Frameworks(JsonElement project)
    {
        return project.GetProperty("targetFrameworks")
            .EnumerateArray()
            .Select(framework => framework.GetString())
            .ToList();
    }

    private static JsonElement Project(JsonDocument document, string name)
    {
        return document.RootElement.GetProperty("projects")
            .EnumerateArray()
            .Single(project => project.GetProperty("name")
                .GetString() == name);
    }
}
