using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Tests.Checking;
using Zphil.LoadBearing.Tests.Extraction;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Roslyn;

/// <summary>
///     One project file, two target frameworks, over the real <c>MultiTfm</c> fixture solution
///     (<c>MultiTfm.Core</c> targets <c>netstandard2.0;net10.0</c>; <c>MultiTfm.Web</c> is single-framework
///     and references it). These pin that a multi-framework project has exactly one name everywhere it is
///     read — the model's <see cref="TypeNode.ProjectName" />, the project list, a project reference, and an
///     <c>arch.Project</c> selection — and that the one thing normalization does <em>not</em> cure, the
///     shared types taking one framework's facts, is disclosed rather than silent.
/// </summary>
/// <remarks>
///     In the <see cref="SerialCollection">Serial</see> collection: every test here loads an MSBuild
///     workspace, and the first opens one of its own.
/// </remarks>
[Collection("Serial")]
public sealed class MultiTargetFrameworkTests
{
    private const string Core = "MultiTfm.Core";
    private const string Web = "MultiTfm.Web";
    private const string Widget = "MultiTfm.Core.Widget";

    private static readonly string SolutionPath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "TestSolutions", "MultiTfm", "MultiTfm.sln");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OpenSolutionAsync_MultiTargetedProject_YieldsRoslynTfmDiscriminatedNames()
    {
        // This pins ROSLYN's behaviour, not LoadBearing's — the premise every other test here inverts.
        // MSBuildProjectLoader appends a "(tfm)" discriminator whenever one project file yields more than
        // one Project, which is why the fixture's one csproj arrives under two unusable names. It therefore
        // opens its OWN workspace: WorkspaceLoader normalizes, so loading through it would prove nothing.
        using var workspace = MSBuildWorkspace.Create();

        Solution solution = await workspace.OpenSolutionAsync(SolutionPath, cancellationToken: Ct);

        solution.Projects
            .Select(project => project.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe(["MultiTfm.Core(net10.0)", "MultiTfm.Core(netstandard2.0)", "MultiTfm.Web"]);
    }

    [Fact]
    public async Task LoadAsync_MultiTargetedProject_NormalizesEveryNameToItsCsprojName()
    {
        // The load boundary is where the discriminator comes off, so this loads for real rather than
        // through the shared pool: the normalization is the subject.
        using LoadedSolution loaded = await WorkspaceLoader.LoadAsync(SolutionPath, ct: Ct);

        // Both of the csproj's Projects now answer to the csproj's own name — nothing downstream can read
        // a discriminated one — and the discriminator survives on the side, as the framework map.
        loaded.Solution.Projects
            .Select(project => project.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe([Core, Core, Web]);

        var coreProjectIds = loaded.Solution.Projects
            .Where(project => project.Name == Core)
            .Select(project => project.Id)
            .ToList();
        var frameworks = loaded.TargetFrameworks;
        coreProjectIds
            .Select(id => frameworks[id])
            .OrderBy(framework => framework, StringComparer.Ordinal)
            .ShouldBe(["net10.0", "netstandard2.0"]);

        // The single-framework project was never discriminated, so it is not in the map at all.
        ProjectId webProjectId = loaded.Solution.Projects
            .Single(project => project.Name == Web)
            .Id;
        loaded.TargetFrameworks.ContainsKey(webProjectId)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task ExtractFromSolutionAsync_MultiTargetedProject_AttributesItsTypesToTheUndiscriminatedName()
    {
        CodebaseModel model = await ExtractAsync();

        // The field report's exact failure: a type attributed to "MultiTfm.Core(net10.0)" is invisible to
        // arch.Project("MultiTfm.Core"), and no spelling of the discriminated name is even typeable.
        model.Type(Widget)
            .ProjectName.ShouldBe(Core);

        // The framework-exclusive type rides in under the same name, so the union really is a union.
        model.Type("MultiTfm.Core.ModernOnly")
            .ProjectName.ShouldBe(Core);

        model.Projects
            .Select(project => project.Name)
            .ShouldBe([Core, Web]);
    }

    [Fact]
    public async Task Check_ProjectRuleOverMultiTargetedProject_SelectsItsTypes()
    {
        CodebaseModel model = await ExtractAsync();

        // A rule that must red, and must red NAMING Widget: a green here could otherwise be an
        // empty-subject false negative rather than a selection that worked.
        RuleResult failing = Checker.Run(model, arch =>
                arch.Rule("naming/multi-tfm")
                    .Enforce(arch.Project(Core)
                        .MustHavePrefix("Zzz"))
                    .Because("b"))
            .Single();
        failing.ShapeSubjects()
            .ShouldContain(Widget);

        // And a rule that must hold, over the same subject: the selection is non-empty and behaves.
        Checker.Run(model, arch =>
                arch.Rule("layering/multi-tfm")
                    .Enforce(arch.Project(Core)
                        .MustNotReference(arch.Namespace("MultiTfm.Web.*")))
                    .Because("b"))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public async Task ExtractFromSolutionAsync_MultiTargetedProject_RecordsNoCrossProjectConflationNote()
    {
        CodebaseModel model = await ExtractAsync();

        // Both compilations of the csproj now carry one project name, so the same-FQN conflation note —
        // which fires only across DIFFERENT project names — never runs. One csproj is never a conflation.
        model.MergeNotes
            .ShouldNotContain(note => note.Contains("is declared by projects", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExtractFromSolutionAsync_MultiTargetedProject_RecordsOneMultiFrameworkNoteNamingTheWinningFramework()
    {
        CodebaseModel model = await ExtractAsync();

        // The one thing normalization deliberately does not cure: Widget is declared by both frameworks and
        // can only carry one framework's facts. net10.0 wins on the extractor's ordinal framework order, not
        // on the csproj's declaration order — which lists netstandard2.0 first, precisely so this pin means
        // something. ModernOnly, which only net10.0 declares, collapsed nothing and adds no note.
        model.MergeNotes.ShouldBe([
            "Project 'MultiTfm.Core' targets 'net10.0' and 'netstandard2.0'; the types they share take "
            + "their facts from 'net10.0' (the first extracted), so a rule about them is checked against "
            + "that framework alone."
        ]);
    }

    [Fact]
    public async Task ExtractFromSolutionAsync_SingleTargetedConsumer_ResolvesItsProjectReferenceToTheUndiscriminatedName()
    {
        CodebaseModel model = await ExtractAsync();

        // Reference names are resolved through the same Project.Name, so a consumer's ProjectReference to a
        // multi-framework project names it once rather than naming a framework of it.
        model.Projects
            .Single(project => project.Name == Web)
            .ProjectReferences.ShouldBe([Core]);
    }

    // The shared read: the pool loads the fixture once for the whole class, and the framework map rides
    // with the snapshot into extraction exactly as the warm server passes it.
    private static async Task<CodebaseModel> ExtractAsync()
    {
        WorkspaceSnapshot snapshot = await WarmWorkspacePool.GetCurrentAsync(SolutionPath, Ct);
        return await CodebaseExtractor.ExtractFromSolutionAsync(
            snapshot.Solution, null, snapshot.TargetFrameworks, null, Ct);
    }
}
