using System.Text.Json;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     End-to-end <c>graph</c> against the LinkedApp fixture — the one solution in the suite that compiles
///     a single source file into more than one project, which is the shape neither MyApp nor any synthetic
///     compilation can produce: <c>LinkedApp.Tool</c> carries
///     <c>&lt;Compile Include="..\LinkedApp.Core\Shared\Widget.cs"&gt;</c> and declares no
///     <c>ProjectReference</c> to Core at all.
/// </summary>
/// <remarks>
///     <para>
///         <b>Both directions of the defect, on one bed.</b> Extraction attributes a multiply-declared type
///         to its first declarer, so Tool's reference to its <em>own</em> compiled-in <c>Widget</c> resolves
///         to a node stamped <c>LinkedApp.Core</c> — which the survey used to render as
///         <c>LinkedApp.Tool -&gt; LinkedApp.Core</c>, an edge no project file declares — while saying
///         nothing at all about the attribution that produced it, so a reader could not see that
///         <c>arch.Project("LinkedApp.Tool")</c> will miss the type.
///     </para>
///     <para>
///         <b>Why the absent edge is the assertion rather than a count.</b> Tool's only outward type
///         reference is the linked one, so a correct survey renders <em>no</em> Tool→Core edge whatsoever
///         and no ordering accident inside the grouping can satisfy that. <c>LinkedApp.Client</c> is the
///         control beside it: a real <c>ProjectReference</c> and a reference to a type only Core declares,
///         so the suppression is measured against an edge that must survive it.
///     </para>
///     <para>
///         The fixture is leased through <see cref="TempFixtureWorkspace" /> rather than read in place
///         because it is restored per class like every other <c>TestSolutions</c> bed;
///         <see cref="GraphCommandTests" /> pins the document's shape against MyApp, and this pins the two
///         things MyApp has no example of.
///     </para>
/// </remarks>
[Collection("Serial")]
public sealed class LinkedSourceGraphE2ETests
{
    private const string MultiplyDeclaredHeading = "Types declared by more than one project:";

    [Fact]
    public async Task Graph_ProjectSharingASourceFileWithAnother_RendersNoEdgeBetweenThem()
    {
        // Arrange
        using var fixture = new TempFixtureWorkspace("TestSolutions/LinkedApp", "LinkedApp.slnx");

        // Act
        CliResult result = await CliRunner.InvokeAsync("graph", fixture.SolutionPath, "--json");

        // Assert — the genuine edge and only the genuine edge. Tool declares no reference to Core and
        // reaches nothing outside itself, so any Tool→Core row here is the phantom.
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        ProjectEdges(document)
            .ShouldBe([("LinkedApp.Client", "LinkedApp.Core", 1)]);
    }

    [Fact]
    public async Task Graph_MultiplyDeclaredType_IsReadableFromTheDocumentAlone()
    {
        // Arrange
        using var fixture = new TempFixtureWorkspace("TestSolutions/LinkedApp", "LinkedApp.slnx");

        // Act
        CliResult result = await CliRunner.InvokeAsync("graph", fixture.SolutionPath, "--json");

        // Assert — the whole content of the check-side note, as data: the type, every project declaring it,
        // and which one's facts an arch.Project selection follows. A reader holding this document alone can
        // tell that a subject anchored on LinkedApp.Tool will not select Widget.
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        JsonElement entry = document.RootElement.GetProperty("multiplyDeclaredTypes")
            .EnumerateArray()
            .ShouldHaveSingleItem();

        entry.GetProperty("type")
            .GetString()
            .ShouldBe("LinkedApp.Shared.Widget");
        entry.GetProperty("declaredBy")
            .EnumerateArray()
            .Select(project => project.GetString())
            .ShouldBe(["LinkedApp.Core", "LinkedApp.Tool"]);
        entry.GetProperty("factsFollow")
            .GetString()
            .ShouldBe("LinkedApp.Core");
    }

    [Fact]
    public async Task Graph_SkeletonGrain_ReportsTheElidedCoverageStatementAsACount()
    {
        // Arrange — the coverage statement scales with the codebase (45 entries on the field-test's MathNet
        // survey), so it rides the same rung as the external rows. An elided section must not read as an
        // absent one, which for this key would say the opposite of the truth.
        using var fixture = new TempFixtureWorkspace("TestSolutions/LinkedApp", "LinkedApp.slnx");

        // Act
        CliResult result = await CliRunner.InvokeAsync("graph", fixture.SolutionPath, "--json", "--skeleton");

        // Assert
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.RootElement.TryGetProperty("multiplyDeclaredTypes", out _)
            .ShouldBeFalse();
        document.RootElement.GetProperty("multiplyDeclaredTypeCount")
            .GetInt32()
            .ShouldBe(1);
    }

    [Fact]
    public async Task Graph_ScopedToTheLosingProject_KeepsTheEntryThatExplainsWhatItWillMiss()
    {
        // Arrange — the scope names LinkedApp.Tool, whose copy of Widget lost the attribution. Keying the
        // narrowing on the winner alone would drop the entry from exactly the scope whose author needs it.
        using var fixture = new TempFixtureWorkspace("TestSolutions/LinkedApp", "LinkedApp.slnx");

        // Act
        CliResult result = await CliRunner.InvokeAsync(
            "graph", fixture.SolutionPath, "--json", "--projects", "LinkedApp.Tool");

        // Assert — the roster narrows to one project; the entry survives and still names the declarer
        // outside it, which is the half that explains why the type is missing.
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.RootElement.GetProperty("projects")
            .EnumerateArray()
            .Select(project => project.GetProperty("name")
                .GetString())
            .ShouldBe(["LinkedApp.Tool"]);
        document.RootElement.GetProperty("multiplyDeclaredTypes")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("type")
                .GetString())
            .ShouldBe(["LinkedApp.Shared.Widget"]);
    }

    [Fact]
    public async Task Graph_HumanSurvey_CarriesTheMultiplyDeclaredSectionBetweenTheEdgesAndTheNamespaces()
    {
        // Arrange
        using var fixture = new TempFixtureWorkspace("TestSolutions/LinkedApp", "LinkedApp.slnx");

        // Act
        CliResult result = await CliRunner.InvokeAsync("graph", fixture.SolutionPath);

        // Assert — one line per type, naming every declarer and the winner, so the terminal reader gets the
        // same fact the document carries rather than having to reach for --json.
        result.ShouldSucceed();
        Section(result.Out, MultiplyDeclaredHeading)
            .ShouldBe([
                "  LinkedApp.Shared.Widget — declared by LinkedApp.Core, LinkedApp.Tool; "
                + "facts follow LinkedApp.Core"
            ]);
    }

    [Fact]
    public async Task Graph_HumanSurveyAtSkeletonGrain_ElidesTheSectionRatherThanDroppingIt()
    {
        // Arrange
        using var fixture = new TempFixtureWorkspace("TestSolutions/LinkedApp", "LinkedApp.slnx");

        // Act
        CliResult result = await CliRunner.InvokeAsync("graph", fixture.SolutionPath, "--skeleton");

        // Assert — the formatter's standing rule: no section ever disappears, and a coarser grain says what
        // it left out and how to get it back.
        result.ShouldSucceed();
        Section(result.Out, MultiplyDeclaredHeading)
            .ShouldBe([
                "  (elided at skeleton grain — rerun without --skeleton for the multiply-declared types)"
            ]);
    }

    // ── harness ───────────────────────────────────────────────────────────────────────────────────────────

    private static IReadOnlyList<(string Source, string Target, int References)> ProjectEdges(JsonDocument document)
    {
        return document.RootElement.GetProperty("projectEdges")
            .EnumerateArray()
            .Select(edge => (
                edge.GetProperty("source")
                    .GetString()!,
                edge.GetProperty("target")
                    .GetString()!,
                edge.GetProperty("references")
                    .GetInt32()))
            .ToList();
    }

    // One section of the human survey: the lines between its heading and the blank line closing it.
    private static IReadOnlyList<string> Section(string output, string heading)
    {
        return output
            .NormalizedTrimmed()
            .Split('\n')
            .SkipWhile(line => !line.Equals(heading, StringComparison.Ordinal))
            .Skip(1)
            .TakeWhile(line => line.Length > 0)
            .ToList();
    }
}
