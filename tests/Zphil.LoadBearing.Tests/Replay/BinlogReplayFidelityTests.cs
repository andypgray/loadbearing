using Microsoft.CodeAnalysis;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Replay;
using Zphil.LoadBearing.Tests.Extraction;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Replay;

/// <summary>
///     The Phase 12 D1 fidelity gate: a binlog replayed into an <c>AdhocWorkspace</c> must produce the
///     same <see cref="CodebaseModel" /> the MSBuildWorkspace path produces from the same tree — edge for
///     edge, external for external, site for site. All tests share one lazily-built binlog fixture and
///     run sequentially in this one class, so the freshness test may mutate the shared copy in place and
///     revert it without racing the others. Pinned facts are the spec.
/// </summary>
/// <remarks>
///     In the "Serial" collection: this class loads an MSBuild workspace (a BuildHost child) and shares the
///     assembly-wide binlog fixture with <see cref="BinlogCaptureStoreTests" />, which mutates that same
///     tree — so per <see cref="TestSupport.SerialCollection" /> both must live in the one serial world to
///     never run at once.
/// </remarks>
[Collection("Serial")]
public sealed class BinlogReplayFidelityTests
{
    private static BinlogFixtureWorkspace Fixture => BinlogFixtureWorkspace.Instance;

    [Fact]
    public async Task Replay_SameTreeAsMsBuildWorkspace_ProducesIdenticalModel()
    {
        // Arrange — extract the SAME temp copy both ways, through the one shared extractor path.
        using LoadedSolution loaded = await WorkspaceLoader.LoadAsync(Fixture.SolutionPath);
        CodebaseModel viaMsBuild = await CodebaseExtractor.ExtractFromSolutionAsync(loaded.Solution);

        using ReplayedSolution replayed = BinlogReplayer.Replay(Fixture.BinlogPath);
        CodebaseModel viaReplay = await CodebaseExtractor.ExtractFromSolutionAsync(replayed.Solution);

        // Act
        string msBuildDump = ModelDump.Render(viaMsBuild);
        string replayDump = ModelDump.Render(viaReplay);

        // Assert — byte-for-byte, raw paths included so a slash/spelling drift fails here (never papered over).
        replayDump.ShouldBe(msBuildDump);
    }

    [Fact]
    public async Task Replay_ConstructorEdges_MatchTheMsBuildWorkspace()
    {
        // The construction channel (GRAMMAR §4.5) survives the binlog-replay path — an explicit assertion
        // beside the whole-model dump equality above, so a dropped ctor channel is a named failure not a diff.
        using ReplayedSolution replayed = BinlogReplayer.Replay(Fixture.BinlogPath);
        CodebaseModel model = await CodebaseExtractor.ExtractFromSolutionAsync(replayed.Solution);

        model.HasConstructorEdge("MyApp.Domain.OrderService", "MyApp.Web.HomeController").ShouldBeTrue();
        model.ConstructorEdge("MyApp.Web.InvoiceController", "System.Data.DataTable")
            .Constructed.IsExternal.ShouldBeTrue();
    }

    [Fact]
    public async Task Replay_AfterAppendingTypePostBuild_ReflectsCurrentDiskNotCapturedText()
    {
        // Arrange — append a new type to a source file already covered by the binlog's csc file list, AFTER
        // the binlog was built. Mutate the shared copy in place; a try/finally reverts it for the others.
        string moneySource = Fixture.PathOf("MyApp.Domain", "Money.cs");
        string original = File.ReadAllText(moneySource);
        try
        {
            File.WriteAllText(moneySource, $"{original}{Environment.NewLine}public sealed class ReplayFreshnessProbe {{ }}{Environment.NewLine}");

            // Act — replay the UNCHANGED binlog; text is read from current disk at materialisation time.
            using ReplayedSolution replayed = BinlogReplayer.Replay(Fixture.BinlogPath);
            CodebaseModel model = await CodebaseExtractor.ExtractFromSolutionAsync(replayed.Solution);

            // Assert — the post-capture type is in the model, proving text is not embedded in the binlog.
            model.Types.Select(t => t.FullName).ShouldContain("MyApp.Domain.ReplayFreshnessProbe");
        }
        finally
        {
            File.WriteAllText(moneySource, original);
        }

        // And the revert is visible to a fresh replay — the freshness contract cuts both ways.
        using ReplayedSolution reverted = BinlogReplayer.Replay(Fixture.BinlogPath);
        CodebaseModel afterRevert = await CodebaseExtractor.ExtractFromSolutionAsync(reverted.Solution);
        afterRevert.Types.Select(t => t.FullName).ShouldNotContain("MyApp.Domain.ReplayFreshnessProbe");
    }

    [Fact]
    public async Task Replay_DisposedWrapper_LeavesSolutionUsable()
    {
        // Arrange
        ReplayedSolution replayed = BinlogReplayer.Replay(Fixture.BinlogPath);
        CodebaseModel before = await CodebaseExtractor.ExtractFromSolutionAsync(replayed.Solution);

        // Act — dispose the wrapper (workspace + reader), then keep using the Solution.
        replayed.Dispose();
        CodebaseModel after = await CodebaseExtractor.ExtractFromSolutionAsync(replayed.Solution);

        // Assert — a Roslyn Solution outlives its workspace (the repo-wide LoadedSolution contract).
        after.Types.Count.ShouldBe(before.Types.Count);
    }

    [Fact]
    public void Replay_EveryProject_HasCsprojFilePathAndBuiltOutputAssembly()
    {
        // Arrange
        using ReplayedSolution replayed = BinlogReplayer.Replay(Fixture.BinlogPath);

        // Assert — SolutionReader populates FilePath (csproj) and OutputFilePath (the obj-path intermediate
        // assembly) with no backfill needed; both must be real files on disk. Only the three C# projects
        // survive the C#-only replay filter, keyed on the extension-less MSBuild-parity names.
        replayed.Solution.Projects.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal)
            .ShouldBe(["MyApp.Domain", "MyApp.Legacy.Billing", "MyApp.Web"]);

        foreach (Project project in replayed.Solution.Projects)
        {
            project.FilePath.ShouldNotBeNull();
            project.FilePath!.ShouldEndWith(".csproj");
            File.Exists(project.FilePath).ShouldBeTrue($"csproj should exist on disk: {project.FilePath}");

            project.OutputFilePath.ShouldNotBeNull();
            File.Exists(project.OutputFilePath!).ShouldBeTrue(
                $"built output assembly should exist on disk: {project.OutputFilePath}");
        }
    }
}