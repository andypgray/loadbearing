using Microsoft.CodeAnalysis;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Roslyn;

/// <summary>
///     <see cref="ProjectLoadFailures.Detect" />'s two arms over a pure <see cref="AdhocWorkspace" /> graph
///     plus a real solution file on disk — the predicate the fail-closed gate now keys on, in place of
///     matching MSBuild message text.
/// </summary>
/// <remarks>
///     <para>
///         The shapes asserted here are the ones a real load was measured to produce, not ones invented to
///         fit the code: a project whose evaluation failed carries no evaluated output path and no
///         intermediate assembly path, while every healthy project measured carried both — including a
///         non-SDK-style .NET Framework project and the <c>netstandard2.0</c> leg of a multi-target-framework
///         one, which are the two healthy shapes that would have been refused by a predicate keyed on
///         reference or analyzer counts instead.
///     </para>
///     <para>
///         The whole-solution behaviour is exercised end-to-end by <c>PartialLoadWorkspaceE2ETests</c> over a
///         solution that really does load partially; what is pinned here is the predicate itself, so a change
///         to it reds a fast unit test rather than only a minutes-long workspace one.
///     </para>
/// </remarks>
public sealed class ProjectLoadFailuresTests
{
    [Fact]
    public void Detect_ProjectWithAnEvaluatedOutputPath_IsNotAFailure()
    {
        // The negative control. A healthy project carries an evaluated output path whatever its reference or
        // analyzer counts — which is exactly why those counts are not the predicate: two healthy projects
        // measured had zero analyzer references, and one had four metadata references.
        using var workspace = new AdhocWorkspace();
        Solution solution = WithProjects(workspace, Healthy("P", @"C:\repo\P\P.csproj"));

        ProjectLoadFailures.Detect(solution, null)
            .ShouldBeEmpty();
    }

    [Fact]
    public void Detect_ProjectWithNeitherOutputPath_IsAFailure()
    {
        // Roslyn's CreateEmpty shape: measured on a project naming an unresolvable SDK and on one whose
        // csproj XML is malformed. Both loaded as a Project with no documents, no analyzers and both paths
        // null, so the model is missing that project entirely while the load still "succeeded".
        using var workspace = new AdhocWorkspace();
        Solution solution = WithProjects(workspace, Empty("P", @"C:\repo\P\P.csproj"));

        ProjectLoadFailures.Detect(solution, null)
            .ShouldBe([@"C:\repo\P\P.csproj"]);
    }

    [Fact]
    public void Detect_ProjectWithAnOutputPathButNoIntermediateOne_IsNotAFailure()
    {
        // Both facts are asked for rather than either, so a false positive — a refusal of a healthy solution,
        // the very defect this predicate replaces — needs two independent Roslyn facts to go missing at once.
        // This is the direction that separates the two spellings: under `||` it would red.
        using var workspace = new AdhocWorkspace();
        Solution solution = WithProjects(
            workspace,
            Empty("HasOutput", @"C:\repo\A\A.csproj").WithOutputFilePath(@"C:\repo\A\bin\A.dll"));

        ProjectLoadFailures.Detect(solution, null)
            .ShouldBeEmpty();
    }

    [Fact]
    public void Detect_MultiTargetFrameworkProject_CollapsesToOneCsprojPath()
    {
        // One csproj behind several Projects: the reader has one file to go and fix, so the answer names it
        // once rather than once per framework.
        using var workspace = new AdhocWorkspace();
        Solution solution = WithProjects(
            workspace,
            Empty("P(net10.0)", @"C:\repo\P\P.csproj"),
            Empty("P(netstandard2.0)", @"C:\repo\P\P.csproj"));

        ProjectLoadFailures.Detect(solution, null)
            .ShouldBe([@"C:\repo\P\P.csproj"]);
    }

    [Fact]
    public void Detect_SolutionDeclaresAProjectThatDidNotLoad_NamesTheDeclaredPath()
    {
        // Arm 1, over a real solution file: a member whose .csproj is absent never becomes a Project at all,
        // so the loaded solution has nothing to inspect and only the declared membership can say it is gone.
        using TempDirectory temp = TestTempRoot.Fresh("declared-absent");
        string solutionPath = WriteSolution(temp, "Present", "Absent");
        using var workspace = new AdhocWorkspace();
        Solution solution = WithProjects(workspace, Healthy("Present", temp.PathOf("Present", "Present.csproj")));

        ProjectLoadFailures.Detect(solution, solutionPath)
            .ShouldBe([temp.PathOf("Absent", "Absent.csproj")]);
    }

    [Fact]
    public void Detect_SolutionWhoseEveryMemberLoaded_IsClean()
    {
        using TempDirectory temp = TestTempRoot.Fresh("declared-present");
        string solutionPath = WriteSolution(temp, "One", "Two");
        using var workspace = new AdhocWorkspace();
        Solution solution = WithProjects(
            workspace,
            Healthy("One", temp.PathOf("One", "One.csproj")),
            Healthy("Two", temp.PathOf("Two", "Two.csproj")));

        ProjectLoadFailures.Detect(solution, solutionPath)
            .ShouldBeEmpty();
    }

    [Fact]
    public void Detect_SolutionFilter_SkipsTheDeclaredMembershipArmEntirely()
    {
        // A .slnf legitimately loads a subset — measured: a filter naming one of two members loads exactly
        // that one — so an arm that read the underlying solution's membership would refuse every filtered
        // solution. SolutionProjectFileParser.OwnsFormat is the guard, and this is what it guards.
        using TempDirectory temp = TestTempRoot.Fresh("declared-filtered");
        WriteSolution(temp, "Kept", "Dropped");
        string filterPath = temp.PathOf("Filter.slnf");
        File.WriteAllText(
            filterPath,
            "{\"solution\":{\"path\":\"Solution.sln\",\"projects\":[\"Kept\\\\Kept.csproj\"]}}");
        using var workspace = new AdhocWorkspace();
        Solution solution = WithProjects(workspace, Healthy("Kept", temp.PathOf("Kept", "Kept.csproj")));

        ProjectLoadFailures.Detect(solution, filterPath)
            .ShouldBeEmpty();
    }

    [Fact]
    public void Detect_BothArmsFire_ReturnsOnePathPerProjectInOrdinalOrder()
    {
        // The two arms are one answer, deduplicated by this OS's path rule and ordinal-sorted, so a refusal
        // reads the same however the failures arose and however the solution file happened to order them.
        using TempDirectory temp = TestTempRoot.Fresh("both-arms");
        string solutionPath = WriteSolution(temp, "Zed", "Absent");
        using var workspace = new AdhocWorkspace();
        Solution solution = WithProjects(workspace, Empty("Zed", temp.PathOf("Zed", "Zed.csproj")));

        ProjectLoadFailures.Detect(solution, solutionPath)
            .ShouldBe([temp.PathOf("Absent", "Absent.csproj"), temp.PathOf("Zed", "Zed.csproj")]);
    }

    // ── harness ───────────────────────────────────────────────────────────────────────────────────────────

    // A project the design-time build evaluated. Only the output path is set: CompilationOutputInfo has no
    // public constructor, so an AdhocWorkspace project's intermediate assembly path is always null — which
    // makes every "healthy" project here the strictest case the predicate can be handed, one carrying only
    // one of the two facts.
    private static ProjectInfo Healthy(string name, string filePath)
    {
        return Empty(name, filePath)
            .WithOutputFilePath(Path.Combine(Path.GetDirectoryName(filePath)!, "bin", name + ".dll"));
    }

    // Roslyn's CreateEmpty shape: no documents, no references, and neither output path.
    private static ProjectInfo Empty(string name, string filePath)
    {
        return ProjectInfo.Create(
            ProjectId.CreateNewId(), VersionStamp.Default, name, name, LanguageNames.CSharp, filePath);
    }

    private static Solution WithProjects(AdhocWorkspace workspace, params ProjectInfo[] projects)
    {
        foreach (ProjectInfo project in projects) workspace.AddProject(project);
        return workspace.CurrentSolution;
    }

    // A classic .sln declaring one project line per name. Only the project paths are read, so the rest of the
    // file is the minimum a real solution carries.
    private static string WriteSolution(TempDirectory temp, params string[] projectNames)
    {
        var lines = new List<string>
        {
            "",
            "Microsoft Visual Studio Solution File, Format Version 12.00",
            "# Visual Studio Version 17"
        };
        foreach (string name in projectNames)
        {
            lines.Add(
                $"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"{name}\", "
                + $"\"{name}\\{name}.csproj\", \"{{11111111-1111-1111-1111-111111111111}}\"");
            lines.Add("EndProject");
        }

        string solutionPath = temp.PathOf("Solution.sln");
        File.WriteAllLines(solutionPath, lines);
        return solutionPath;
    }
}
