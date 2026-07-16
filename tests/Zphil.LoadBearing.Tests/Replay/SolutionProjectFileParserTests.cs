using Shouldly;
using Xunit;
using Zphil.LoadBearing.Roslyn.Replay;

namespace Zphil.LoadBearing.Tests.Replay;

/// <summary>
///     Pure-string tests for <see cref="SolutionProjectFileParser" /> — no fixture, no disk. They pin the
///     textual csproj-membership extraction the capture's coverage check depends on: both solution formats,
///     both slash spellings, and the rule that only <c>.csproj</c> entries count (solution folders and other
///     project kinds are ignored).
/// </summary>
public sealed class SolutionProjectFileParserTests
{
    // A path root that need not exist — ParseCsprojMembers resolves textually via Path.GetFullPath.
    private static readonly string SolutionDirectory = Path.Combine(Path.GetTempPath(), "sln-parse-tests");

    [Fact]
    public void ParseCsprojMembers_ClassicSln_ReturnsCsprojsIgnoringFolders()
    {
        // Arrange — a Domain project (backslash), a Web project (forward slash), and a solution folder.
        const string text = """
                            Microsoft Visual Studio Solution File, Format Version 12.00
                            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Alpha", "Alpha\Alpha.csproj", "{11111111-1111-1111-1111-111111111111}"
                            EndProject
                            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Beta", "nested/Beta/Beta.csproj", "{22222222-2222-2222-2222-222222222222}"
                            EndProject
                            Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "Solution Items", "Solution Items", "{33333333-3333-3333-3333-333333333333}"
                            EndProject
                            """;

        // Act
        var members = SolutionProjectFileParser.ParseCsprojMembers(text, ".sln", SolutionDirectory);

        // Assert — both csprojs (either slash spelling resolves), the folder dropped.
        members.ShouldBe([
            Path.GetFullPath(Path.Combine(SolutionDirectory, "Alpha", "Alpha.csproj")),
            Path.GetFullPath(Path.Combine(SolutionDirectory, "nested", "Beta", "Beta.csproj"))
        ]);
    }

    [Fact]
    public void ParseCsprojMembers_Slnx_ReturnsCsprojsIgnoringNonCsprojAndNesting()
    {
        // Arrange — a root project, a folder-nested project, and a non-csproj entry.
        const string text = """
                            <Solution>
                              <Project Path="Alpha\Alpha.csproj" />
                              <Folder Name="/Shared/">
                                <Project Path="nested/Beta/Beta.csproj" />
                              </Folder>
                              <Project Path="docs/Readme.md" />
                            </Solution>
                            """;

        // Act
        var members = SolutionProjectFileParser.ParseCsprojMembers(text, ".slnx", SolutionDirectory);

        // Assert — both csprojs (nesting flattened, both slash spellings), the markdown dropped.
        members.ShouldBe([
            Path.GetFullPath(Path.Combine(SolutionDirectory, "Alpha", "Alpha.csproj")),
            Path.GetFullPath(Path.Combine(SolutionDirectory, "nested", "Beta", "Beta.csproj"))
        ]);
    }

    [Fact]
    public void ParseCsprojMembers_NoProjects_ReturnsEmpty()
    {
        // Arrange — a header-only solution with no project lines.
        const string text = "Microsoft Visual Studio Solution File, Format Version 12.00\n";

        // Act + Assert
        SolutionProjectFileParser.ParseCsprojMembers(text, ".sln", SolutionDirectory).ShouldBeEmpty();
    }
}