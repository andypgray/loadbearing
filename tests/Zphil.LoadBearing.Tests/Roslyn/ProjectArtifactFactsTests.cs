using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Caching;
using Zphil.LoadBearing.Roslyn.Extraction;
using Zphil.LoadBearing.Roslyn.Solutions;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Roslyn;

/// <summary>
///     The evaluated artifact facts over the real <c>MultiTfm</c> fixture solution: the frameworks a project
///     declares, the packages it declares, whether it packs and whether its restore locks — each read from
///     an MSBuild evaluation rather than from the project file's XML, and each carrying the
///     <c>file:line</c> that decided it.
/// </summary>
/// <remarks>
///     In the <see cref="SerialCollection">Serial</see> collection: every test here loads an MSBuild
///     workspace.
/// </remarks>
[Collection("Serial")]
public sealed class ProjectArtifactFactsTests
{
    private const string Core = "MultiTfm.Core";
    private const string Web = "MultiTfm.Web";

    private static readonly string SolutionPath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "TestSolutions", "MultiTfm", "MultiTfm.sln");

    private static readonly Lazy<Task<CodebaseModel>> Codebase = new(ExtractAsync);

    [Fact]
    public async Task ExtractFromSolutionAsync_MultiTargetedProject_DeclaresBothFrameworksAtItsOwnDeclaration()
    {
        CodebaseModel model = await Codebase.Value;

        // Ordinal order, never the csproj's — which declares netstandard2.0 first — so the first entry stays
        // the one a shared type's facts fall to.
        ProjectNode core = model.Projects.Single(project => project.Name == Core);
        core.TargetFrameworks.ShouldBe(["net10.0", "netstandard2.0"]);
        core.TargetFrameworksSite.ShouldNotBeNull()
            .FilePath.ShouldEndWith("MultiTfm.Core.csproj");
    }

    [Fact]
    public async Task ExtractFromSolutionAsync_SdkProjectDeclaringNothing_IsPackableAtItsOwnFileAndDoesNotLock()
    {
        CodebaseModel model = await Codebase.Value;

        // The SDK default nobody wrote, which is the whole reason these facts are evaluated: the project
        // file says nothing about packability, and the answer is still yes. The site degrades to the project
        // rather than naming a file inside whichever SDK ran.
        ProjectNode web = model.Projects.Single(project => project.Name == Web);
        web.IsPackable.ShouldBe(true);
        web.IsPackableSite.ShouldNotBeNull()
            .FilePath.ShouldEndWith("MultiTfm.Web.csproj");
        web.LocksPackages.ShouldBe(false);
        web.PackageReferences.ShouldBeEmpty();
    }

    [Fact]
    public void EvaluateAll_PropertySetInAPropsFileAbove_IsReportedAtThatFilesLine()
    {
        // The case that makes evaluation the only honest reader: nothing in the project file mentions the
        // lock policy, and the answer — and the line a violation would have to cite — is two directories up.
        string root = TestTempRoot.For("artifact-facts-props");
        string projectDirectory = Path.Combine(root, "src", "Widget");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(Path.Combine(root, "src", "Directory.Build.props"), """
                                                                              <Project>
                                                                                  <PropertyGroup>
                                                                                      <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
                                                                                  </PropertyGroup>
                                                                              </Project>
                                                                              """);
        string projectFile = Path.Combine(projectDirectory, "Widget.csproj");
        File.WriteAllText(projectFile, """
                                       <Project Sdk="Microsoft.NET.Sdk">
                                           <PropertyGroup>
                                               <TargetFramework>net10.0</TargetFramework>
                                               <IsPackable>false</IsPackable>
                                           </PropertyGroup>
                                           <ItemGroup>
                                               <PackageReference Include="Shouldly" Version="4.3.0" />
                                           </ItemGroup>
                                       </Project>
                                       """);

        // Act
        ProjectArtifactFacts facts = ProjectFactsEvaluator
            .EvaluateAll([new ProjectEvaluationRequest(projectFile, null)], Ct)
            .Single()
            .ShouldNotBeNull();

        // Assert — the far declaration for the policy, the near one for the opt-out, and the package with
        // the line it is written on.
        facts.LocksPackages.ShouldBeTrue();
        facts.LocksPackagesSite.File.ShouldEndWith(Path.Combine("src", "Directory.Build.props"));
        facts.LocksPackagesSite.Line.ShouldBe(3);
        facts.IsPackable.ShouldBe(false);
        facts.IsPackableSite.ShouldBe(new FragmentSite(projectFile, 4));
        facts.PackageReferences.ShouldBe([new FragmentPackageReference("Shouldly", new FragmentSite(projectFile, 7))]);
    }

    [Fact]
    public void EvaluateAll_ProjectPredatingTheSdk_NormalizesItsFrameworkVersionToTheShortMoniker()
    {
        // A 2003-namespace project states an identifier and a version where an SDK project states a
        // moniker. Nothing downstream should have to know that, so the two spellings meet here.
        string projectDirectory = TestTempRoot.For("artifact-facts-classic");
        Directory.CreateDirectory(projectDirectory);
        string projectFile = Path.Combine(projectDirectory, "Classic.csproj");
        File.WriteAllText(projectFile, """
                                       <?xml version="1.0" encoding="utf-8"?>
                                       <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
                                           <PropertyGroup>
                                               <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
                                               <OutputType>Library</OutputType>
                                           </PropertyGroup>
                                       </Project>
                                       """);

        // Act
        ProjectArtifactFacts facts = ProjectFactsEvaluator
            .EvaluateAll([new ProjectEvaluationRequest(projectFile, null)], Ct)
            .Single()
            .ShouldNotBeNull();

        // Assert — the moniker, sited at the version it was read from. Packability has no answer at all
        // here: a project outside the SDK's pack machinery is not un-packable, it is unanswered.
        facts.TargetFrameworks.ShouldBe(["net48"]);
        facts.TargetFrameworksSite.ShouldBe(new FragmentSite(projectFile, 4));
        facts.IsPackable.ShouldBeNull();
        facts.LocksPackages.ShouldBeFalse();
    }

    private static async Task<CodebaseModel> ExtractAsync()
    {
        WorkspaceSnapshot snapshot = await WarmWorkspacePool.GetCurrentAsync(SolutionPath, CancellationToken.None);
        return await CodebaseExtractor.ExtractFromSolutionAsync(
            snapshot.Solution, null, snapshot.TargetFrameworks, null, CancellationToken.None);
    }
}
