using System.Text.Json;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     End-to-end <c>graph</c> and <c>check</c> against the PolyglotApp fixture — the one solution in the
///     suite holding a project this product cannot read: a real <c>.fsproj</c>, not a renamed
///     <c>.csproj</c>, beside one ordinary C# project.
/// </summary>
/// <remarks>
///     <para>
///         <b>The defect these pin.</b> The survey read what the C#/Roslyn extractor could and said nothing
///         about the rest, so its <c>projects</c> array was simply shorter than the solution: on a real
///         codebase 10 of the 12 projects a solution declared, with no count, no list and no note, and two
///         <c>.fsproj</c> under no key at all. A spec derived from that survey cannot reach whatever ships
///         from those projects, and nothing in the document says so. <c>check</c> had the identical silence
///         from the other side — a clean verdict over a polyglot solution never mentioning the projects it
///         never measured.
///     </para>
///     <para>
///         <b>Why an <c>.fsproj</c> specifically, and why it must survive a real load.</b> F# is the one
///         non-C# kind that reaches a loaded solution at all: it plugs into Roslyn's workspace model while
///         having no Roslyn compiler behind it, so it arrives as a project that supports no compilation.
///         Measured on this bed, it reaches the workspace carrying its output paths, which is why nothing
///         upstream noticed it going missing. The other kinds — <c>.vbproj</c>, <c>.sqlproj</c>,
///         <c>.vcxproj</c>, <c>.shproj</c> — never load at all and are pinned over solution text in
///         <see cref="Roslyn.SolutionProjectFileParserTests" />, where they cost a theory row rather than a
///         second heavyweight bed.
///     </para>
///     <para>
///         The fixture is leased through <see cref="TempFixtureWorkspace" /> like every other
///         <c>TestSolutions</c> bed. <see cref="GraphCommandTests" /> pins the survey document's shape
///         against MyApp, which is all C# and so carries none of these keys.
///     </para>
/// </remarks>
[Collection("Serial")]
public sealed class PolyglotSurveyE2ETests
{
    private const string UnsupportedHeading = "Projects the solution declares that this survey does not cover:";

    [Fact]
    public async Task Graph_SolutionDeclaringAnFsproj_NamesItAndWhyFromTheDocumentAlone()
    {
        // Arrange
        using var fixture = new TempFixtureWorkspace("TestSolutions/PolyglotApp", "PolyglotApp.slnx");

        // Act
        CliResult result = await CliRunner.InvokeAsync("graph", fixture.SolutionPath, "--json");

        // Assert — the whole content of the coverage statement, as data: which project, and why it is not
        // in the survey. A reader holding this document alone can tell a two-project codebase from a survey
        // that read one project of two, which is precisely what they could not do before.
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        JsonElement entry = document.RootElement.GetProperty("unsupportedProjects")
            .EnumerateArray()
            .ShouldHaveSingleItem();

        entry.GetProperty("project")
            .GetString()
            .ShouldBe("PolyglotApp.Fs/PolyglotApp.Fs.fsproj");
        entry.GetProperty("reason")
            .GetString()
            .ShouldBe("not a C# project");
    }

    [Fact]
    public async Task Graph_SolutionDeclaringAnFsproj_SurveysTheCsharpProjectAndOnlyIt()
    {
        // Arrange — the control beside the coverage statement. Naming what was not read is only half the
        // fix; the other half is that what WAS read is unchanged, so the key is an addition to a correct
        // survey rather than a repair to a broken one.
        using var fixture = new TempFixtureWorkspace("TestSolutions/PolyglotApp", "PolyglotApp.slnx");

        // Act
        CliResult result = await CliRunner.InvokeAsync("graph", fixture.SolutionPath, "--json");

        // Assert
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.RootElement.GetProperty("projects")
            .EnumerateArray()
            .Select(project => project.GetProperty("name")
                .GetString())
            .ShouldBe(["PolyglotApp.Core"]);
    }

    [Fact]
    public async Task Graph_SkeletonGrain_KeepsTheCoverageStatementWhole()
    {
        // Arrange — unlike the survey's other two coverage keys this one is NOT elided at any grain: its
        // length scales with the solution rather than the codebase, and the coarser the document the more a
        // reader needs to know it describes part of the solution.
        using var fixture = new TempFixtureWorkspace("TestSolutions/PolyglotApp", "PolyglotApp.slnx");

        // Act
        CliResult result = await CliRunner.InvokeAsync("graph", fixture.SolutionPath, "--json", "--skeleton");

        // Assert — the entries themselves, not a count standing in for them.
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.RootElement.GetProperty("unsupportedProjects")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("project")
                .GetString())
            .ShouldBe(["PolyglotApp.Fs/PolyglotApp.Fs.fsproj"]);
    }

    [Fact]
    public async Task Graph_ScopedToTheCsharpProject_KeepsTheStatementThatBoundsTheSubject()
    {
        // Arrange — --projects scopes the survey's roster. The coverage statement is a fact about the load
        // rather than about the roster, so scoping must not silence it: a scoped survey is still not a
        // survey of the whole solution, and its three trust-stamp neighbours do not scope either.
        using var fixture = new TempFixtureWorkspace("TestSolutions/PolyglotApp", "PolyglotApp.slnx");

        // Act
        CliResult result = await CliRunner.InvokeAsync(
            "graph", fixture.SolutionPath, "--json", "--projects", "PolyglotApp.Core");

        // Assert
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.RootElement.GetProperty("unsupportedProjects")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("project")
                .GetString())
            .ShouldBe(["PolyglotApp.Fs/PolyglotApp.Fs.fsproj"]);
    }

    [Fact]
    public async Task Graph_HumanSurvey_CarriesTheSectionUnderTheRosterItQualifies()
    {
        // Arrange
        using var fixture = new TempFixtureWorkspace("TestSolutions/PolyglotApp", "PolyglotApp.slnx");

        // Act
        CliResult result = await CliRunner.InvokeAsync("graph", fixture.SolutionPath);

        // Assert — one line per project, naming the reason, so the terminal reader gets the same fact the
        // document carries rather than having to reach for --json.
        result.ShouldSucceed();
        Section(result.Out, UnsupportedHeading)
            .ShouldBe(["  PolyglotApp.Fs/PolyglotApp.Fs.fsproj — not a C# project"]);
    }

    [Fact]
    public async Task Check_CleanVerdictOverAPolyglotSolution_StillNamesWhatItNeverMeasured()
    {
        // Arrange — the spec's one rule holds over the C# project, so this report is genuinely clean. That
        // is the whole point: a green check over a solution holding projects the model never contained used
        // to read as a green over the solution, because no rule can be violated in a project that is not
        // there and the report had no way to say one was missing.
        using var fixture = new TempFixtureWorkspace("TestSolutions/PolyglotApp", "PolyglotApp.slnx");

        // Act
        CliResult result = await CliRunner.InvokeAsync(
            "check", fixture.SolutionPath, "--spec", CliRunner.PolyglotSpecDll, "--json");

        // Assert
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        JsonElement entry = document.RootElement.GetProperty("unsupportedProjects")
            .EnumerateArray()
            .ShouldHaveSingleItem();

        entry.GetProperty("project")
            .GetString()
            .ShouldBe("PolyglotApp.Fs/PolyglotApp.Fs.fsproj");
        entry.GetProperty("reason")
            .GetString()
            .ShouldBe("not a C# project");
    }

    [Fact]
    public async Task Check_CleanVerdictOverAPolyglotSolution_DoesNotCallTheModelIncomplete()
    {
        // Arrange — the posture that separates this key from failedProjects/restoreFailedProjects. A
        // project this product cannot read makes the universe smaller, never wrong, so it must not gate:
        // it takes uncheckedProjects' posture, and the run exits on the verdict alone.
        using var fixture = new TempFixtureWorkspace("TestSolutions/PolyglotApp", "PolyglotApp.slnx");

        // Act
        CliResult result = await CliRunner.InvokeAsync(
            "check", fixture.SolutionPath, "--spec", CliRunner.PolyglotSpecDll, "--json");

        // Assert — exit 0, and no incompleteness stamp anywhere near it.
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.RootElement.TryGetProperty("modelIncomplete", out _)
            .ShouldBeFalse();
        document.RootElement.TryGetProperty("failedProjects", out _)
            .ShouldBeFalse();
        document.RootElement.TryGetProperty("restoreFailedProjects", out _)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Status_OverAPolyglotSolution_NamesWhatItsBurndownCannotCount()
    {
        // Arrange — the third document, and the one whose silence reads most like progress: a project this
        // product cannot read contributes no violations to burn down, so its zero is a property of the tool
        // rather than debt the team paid off.
        using var fixture = new TempFixtureWorkspace("TestSolutions/PolyglotApp", "PolyglotApp.slnx");

        // Act
        CliResult result = await CliRunner.InvokeAsync(
            "status", fixture.SolutionPath, "--spec", CliRunner.PolyglotSpecDll, "--json");

        // Assert
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        JsonElement entry = document.RootElement.GetProperty("unsupportedProjects")
            .EnumerateArray()
            .ShouldHaveSingleItem();

        entry.GetProperty("project")
            .GetString()
            .ShouldBe("PolyglotApp.Fs/PolyglotApp.Fs.fsproj");
        entry.GetProperty("reason")
            .GetString()
            .ShouldBe("not a C# project");
    }

    [Fact]
    public async Task StatusHumanChannel_OverAPolyglotSolution_StampsTheBurndownItCannotCover()
    {
        // Arrange — the human twin of the slot above, on the verb whose whole output is counts.
        using var fixture = new TempFixtureWorkspace("TestSolutions/PolyglotApp", "PolyglotApp.slnx");

        // Act
        CliResult result = await CliRunner.InvokeAsync(
            "status", fixture.SolutionPath, "--spec", CliRunner.PolyglotSpecDll);

        // Assert — the whole stamp: the counted lede, the evidence line, and the tail that denies the
        // reading a bare zero invites.
        result.ShouldSucceed();
        result.Out.NormalizedTrimmed()
            .ShouldStartWith(
                "1 project the solution declares was not surveyed:\n"
                + "  PolyglotApp.Fs/PolyglotApp.Fs.fsproj — not a C# project\n"
                + "The burndown below counts only the projects this product can read: these contribute no "
                + "violations and never will, so every count reads low by whatever they hold.");
    }

    [Fact]
    public async Task CheckHumanChannel_OverAPolyglotSolution_StampsTheVerdictItCannotCover()
    {
        // Arrange — the same stamp on check, whose tail denies the other dangerous reading: that a clean
        // verdict over this solution is a clean solution.
        using var fixture = new TempFixtureWorkspace("TestSolutions/PolyglotApp", "PolyglotApp.slnx");

        // Act
        CliResult result = await CliRunner.InvokeAsync(
            "check", fixture.SolutionPath, "--spec", CliRunner.PolyglotSpecDll);

        // Assert
        result.ShouldSucceed();
        result.Out.NormalizedTrimmed()
            .ShouldStartWith(
                "1 project the solution declares was not surveyed:\n"
                + "  PolyglotApp.Fs/PolyglotApp.Fs.fsproj — not a C# project\n"
                + "The verdict below covers only the projects this product can read, so a clean result here "
                + "is not a clean solution.");
    }

    [Fact]
    public async Task CheckSarifChannel_OverAPolyglotSolution_WarnsThatTheScanCoveredPartOfTheSolution()
    {
        // Arrange — the render target with neither an exit code nor a prose channel to fall back on. Code
        // scanning reads the log and nothing else, so a clean SARIF over this solution closes every alert
        // the unreadable project would have raised, with nothing in the document saying it was never
        // scanned. The same silence the two channels above deny, on the surface that cannot be re-read.
        using var fixture = new TempFixtureWorkspace("TestSolutions/PolyglotApp", "PolyglotApp.slnx");
        using TempDirectory temp = TestTempRoot.Fresh("polyglot-sarif");
        string sarifPath = temp.PathOf("polyglot.sarif");

        // Act
        CliResult result = await CliRunner.InvokeAsync(
            "check", fixture.SolutionPath, "--spec", CliRunner.PolyglotSpecDll, "--sarif", sarifPath);

        // Assert — one notification and no other, so a clean polyglot run is pinned to say exactly this
        // much. The level is read off that notification rather than the log text because it carries the
        // posture its JSON twin pins: a project this product cannot read makes the universe smaller, never
        // wrong, so it warns beside the narrowing notice instead of gating like a failed load.
        result.ShouldSucceed();
        JsonElement notification = File.ReadAllText(sarifPath)
            .SarifNotifications()
            .ShouldHaveSingleItem();

        notification.GetProperty("message")
            .GetProperty("text")
            .GetString()
            .ShouldBe(
                "1 project the solution declares was not surveyed, so these results cover part of the "
                + "solution: PolyglotApp.Fs/PolyglotApp.Fs.fsproj (not a C# project)");
        notification.GetProperty("level")
            .GetString()
            .ShouldBe("warning");
    }

    // ── harness ───────────────────────────────────────────────────────────────────────────────────────────

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
