using Shouldly;
using Xunit;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Roslyn;

/// <summary>
///     Tests for <see cref="SolutionProjectFileParser" />, no fixture solution anywhere. They pin the
///     textual csproj-membership extraction both the capture's coverage check and
///     <see cref="SpecExclusion" />'s membership subtraction depend on: both solution formats, both slash
///     spellings, the rule that only <c>.csproj</c> entries count (solution folders and other project kinds
///     are ignored), and which formats the parser owns at all.
///     <para>
///         The <see cref="SolutionProjectFileParser.ParseFilter" /> rows pin the half a solution filter adds:
///         its <em>two</em> resolution bases — the referenced solution against the filter's own directory,
///         every project entry against that solution's — and the four refusals, one of which
///         (<see cref="SolutionProjectFileParser.ReadDeclaredMembership" /> reaching a filter that points at
///         another filter) is a shape Roslyn does not support at all.
///     </para>
///     <para>
///         The <see cref="SolutionProjectFileParser.AnchorDirectory" /> rows are the only ones that need real
///         files, because resolving a filter means reading it: they pin the single directory a whole run
///         resolves its conventions against — baselines, render targets, diff resolution, every relativized
///         path — and that it is never a throw, since the caller asks before the load that owns refusing a
///         bad filter.
///     </para>
/// </summary>
public sealed class SolutionProjectFileParserTests
{
    // A path root that need not exist — ParseCsprojMembers resolves textually via Path.GetFullPath.
    private static readonly string SolutionDirectory = Path.Combine(Path.GetTempPath(), "sln-parse-tests");

    // The filter and the solution it points at deliberately live in different directories, which is the only
    // arrangement that can tell the two resolution bases apart.
    private static readonly string FilterDirectory = Path.Combine(SolutionDirectory, "filters");

    private static readonly string ReferencedSolution =
        Path.GetFullPath(Path.Combine(SolutionDirectory, "solutions", "App.sln"));

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
        SolutionProjectFileParser.ParseCsprojMembers(text, ".sln", SolutionDirectory)
            .ShouldBeEmpty();
    }

    [Fact]
    public void OwnsFormat_TheThreeSolutionFormats_AreOwnedAndNothingElseIs()
    {
        // A membership subtraction depends on telling "the parser cannot read this" from "the solution
        // declares nothing". All three real formats are readable — a .slnf by resolving through to the
        // solution it points at — so only a file that is not a solution at all answers false.
        SolutionProjectFileParser.OwnsFormat("/repo/App.sln")
            .ShouldBeTrue();
        SolutionProjectFileParser.OwnsFormat("/repo/App.SLNX")
            .ShouldBeTrue();
        SolutionProjectFileParser.OwnsFormat("/repo/Filtered.slnf")
            .ShouldBeTrue();
        SolutionProjectFileParser.OwnsFormat("/repo/App")
            .ShouldBeFalse();
    }

    [Fact]
    public void ParseFilter_ProjectsAndSolution_ResolveAgainstTheirOwnBases()
    {
        // The shape Visual Studio writes: the solution path is relative to the filter, and every project
        // path is relative to that solution. Resolving both against one base is the mistake this pins shut —
        // it would look right whenever the filter happens to sit beside the solution, and name paths that
        // exist nowhere as soon as it does not.
        const string filterText = """
                                  {
                                    "solution": {
                                      "path": "../solutions/App.sln",
                                      "projects": [
                                        "Alpha\\Alpha.csproj",
                                        "nested/Beta/Beta.csproj"
                                      ]
                                    }
                                  }
                                  """;

        (string referencedSolution, var requested) = SolutionProjectFileParser.ParseFilter(filterText, FilterDirectory);

        referencedSolution.ShouldBe(ReferencedSolution);
        requested.ShouldBe([
            Path.GetFullPath(Path.Combine(SolutionDirectory, "solutions", "Alpha", "Alpha.csproj")),
            Path.GetFullPath(Path.Combine(SolutionDirectory, "solutions", "nested", "Beta", "Beta.csproj"))
        ]);
    }

    [Fact]
    public void ParseFilter_NoProjectsArray_SelectsNothingRatherThanFailing()
    {
        // An empty selection is not an empty answer: ReadDeclaredMembership reads it as "no filtering at
        // all", which is Roslyn's own rule. The parser's job is only to report that nothing was requested.
        const string filterText = """{ "solution": { "path": "../solutions/App.sln", "projects": [] } }""";

        (string referencedSolution, var requested) = SolutionProjectFileParser.ParseFilter(filterText, FilterDirectory);

        referencedSolution.ShouldBe(ReferencedSolution);
        requested.ShouldBeEmpty();
    }

    [Fact]
    public void ParseFilter_NotJson_RefusesAsMalformed()
    {
        const string filterText = """{ "solution": { "path": "../solutions/App.sln", "projects": [ }""";

        var ex = Should.Throw<FormatException>(() => SolutionProjectFileParser.ParseFilter(filterText, FilterDirectory));

        ex.Message.ShouldBe("The solution filter is not well-formed JSON.");
    }

    [Fact]
    public void ParseFilter_NoSolutionObject_RefusesNamingWhatIsMissing()
    {
        const string filterText = """{ "projects": [] }""";

        var ex = Should.Throw<FormatException>(() => SolutionProjectFileParser.ParseFilter(filterText, FilterDirectory));

        ex.Message.ShouldBe("The solution filter declares no 'solution' object.");
    }

    [Fact]
    public void ParseFilter_SolutionObjectWithoutAPath_RefusesSeparately()
    {
        // Distinct from the row above: the object is there and the path is not, which is a different edit to
        // make and so a different sentence to read.
        const string filterText = """{ "solution": { "projects": [] } }""";

        var ex = Should.Throw<FormatException>(() => SolutionProjectFileParser.ParseFilter(filterText, FilterDirectory));

        ex.Message.ShouldBe("The solution filter declares no solution path.");
    }

    [Fact]
    public void ParseFilter_FilterPointingAtAnotherFilter_Refuses()
    {
        // Roslyn does not support chained filters, so resolving one would produce a membership no load could
        // ever match — a refusal here is the honest answer rather than a downstream mystery.
        const string filterText = """{ "solution": { "path": "../solutions/Inner.slnf", "projects": [] } }""";

        var ex = Should.Throw<FormatException>(() => SolutionProjectFileParser.ParseFilter(filterText, FilterDirectory));

        ex.Message.ShouldBe("A solution filter may not reference another solution filter.");
    }

    [Fact]
    public void AnchorDirectory_Filter_IsTheReferencedSolutionsDirectoryRatherThanTheFiltersOwn()
    {
        // The whole point of an anchor: a filter is a lens on a solution, not a codebase of its own, so a
        // run through one must resolve arch/ and write its output where a run over the solution would. Kept
        // in its own directory — the only arrangement that can tell the two apart — a filter anchored at
        // itself resolves no baseline the team ever committed and lands every written file beside itself.
        using TempDirectory temp = TestTempRoot.Fresh("solution-anchor");
        string solution = WriteReferencedSolution(temp);
        string filter = WriteFilter(temp, """{ "solution": { "path": "../solutions/App.sln", "projects": [] } }""");

        SolutionProjectFileParser.AnchorDirectory(filter)
            .ShouldBe(Path.GetDirectoryName(solution));
    }

    [Fact]
    public void AnchorDirectory_BothSolutionFormats_AreTheirOwnDirectoryAndCostNoRead()
    {
        // An unfiltered run is the overwhelming majority and must answer without touching disk at all —
        // neither of these paths exists, and neither is looked for.
        SolutionProjectFileParser.AnchorDirectory(Path.Combine(SolutionDirectory, "App.sln"))
            .ShouldBe(SolutionDirectory);
        SolutionProjectFileParser.AnchorDirectory(Path.Combine(SolutionDirectory, "App.slnx"))
            .ShouldBe(SolutionDirectory);
    }

    [Fact]
    public void AnchorDirectory_MalformedFilter_FallsBackToItsOwnDirectoryWithoutThrowing()
    {
        // The anchor is computed before the workspace load, and the load is what refuses a malformed filter
        // by name. Throwing here would replace that refusal with a worse-placed one, so the fallback is the
        // filter's own directory and the operator still gets the message the load writes.
        using TempDirectory temp = TestTempRoot.Fresh("solution-anchor");
        string filter = WriteFilter(temp, """{ "solution": { "path": "../solutions/App.sln", """);

        SolutionProjectFileParser.AnchorDirectory(filter)
            .ShouldBe(Path.GetDirectoryName(filter));
    }

    [Fact]
    public void AnchorDirectory_FilterNamingAMissingSolution_FallsBackToItsOwnDirectory()
    {
        // The filter parses and the path resolves — there is simply no solution there. Anchoring at a
        // directory that does not exist would resolve conventions into nowhere and create it on the first
        // write, so a solution that is not on disk is not an anchor.
        using TempDirectory temp = TestTempRoot.Fresh("solution-anchor");
        string filter = WriteFilter(temp, """{ "solution": { "path": "../solutions/Absent.sln", "projects": [] } }""");

        SolutionProjectFileParser.AnchorDirectory(filter)
            .ShouldBe(Path.GetDirectoryName(filter));
    }

    // The filter and the solution it points at, on real disk in the two-directory arrangement the constants
    // above describe. Only the anchor rows need this: resolving a filter means reading it.
    private static string WriteFilter(TempDirectory temp, string filterText)
    {
        string directory = temp.PathOf("filters");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "Selected.slnf");
        File.WriteAllText(path, filterText);
        return path;
    }

    private static string WriteReferencedSolution(TempDirectory temp)
    {
        string directory = temp.PathOf("solutions");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "App.sln");
        File.WriteAllText(path, "Microsoft Visual Studio Solution File, Format Version 12.00\n");
        return path;
    }
}
