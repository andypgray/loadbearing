using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Tests.Roslyn;

/// <summary>
///     <see cref="SolutionExtensions.StripUnresolvedReferences" /> over a pure <see cref="AdhocWorkspace" />
///     graph: an unresolved analyzer reference (which would crash Roslyn's cross-project traversal APIs) is
///     removed from every project and counted, and the cleaned solution is returned without touching disk. The
///     unresolved-metadata arm needs a workspace-resolved reference an AdhocWorkspace cannot mint, so it stays
///     integration-tier — exercised end-to-end by the real workspace/replay loads.
/// </summary>
public sealed class SolutionExtensionsTests
{
    [Fact]
    public void StripUnresolvedReferences_UnresolvedAnalyzer_IsRemovedAndCounted()
    {
        // Arrange — a one-project solution carrying a single unresolved analyzer reference.
        using var workspace = new AdhocWorkspace();
        ProjectInfo projectInfo = ProjectInfo
            .Create(ProjectId.CreateNewId(), VersionStamp.Default, "P", "P", LanguageNames.CSharp)
            .WithAnalyzerReferences([new UnresolvedAnalyzerReference("missing-analyzer.dll")]);
        Project project = workspace.AddProject(projectInfo);

        // Act
        (Solution stripped, int analyzerCount, int metadataCount) = project.Solution.StripUnresolvedReferences();

        // Assert — the unresolved analyzer is gone, counted once; nothing to strip on the metadata side.
        analyzerCount.ShouldBe(1);
        metadataCount.ShouldBe(0);
        stripped.GetProject(project.Id)!.AnalyzerReferences.ShouldBeEmpty();
    }
}