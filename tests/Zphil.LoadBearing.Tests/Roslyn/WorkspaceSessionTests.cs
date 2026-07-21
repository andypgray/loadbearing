using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Roslyn;

/// <summary>
///     Workspace-tier tests for <see cref="WorkspaceSession" /> — the warm, per-call-reconciling solution
///     host. Each test runs against its own restored temp copy of the MyApp fixture (mutating disk between
///     calls), exercising external-edit reconciliation and lifecycle. Serialized with the rest
///     of the workspace-loading suites: every case opens a real <c>MSBuildWorkspace</c>.
/// </summary>
[Collection("Serial")]
public sealed class WorkspaceSessionTests
{
    private const string Domain = "MyApp.Domain";
    private const string Web = "MyApp.Web";
    private const string Billing = "MyApp.Legacy.Billing";

    [Fact]
    public async Task GetCurrentAsync_DocumentEditedOnDisk_ReturnsRefreshedSnapshotWithEdit()
    {
        // Arrange
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var fixture = new TempFixtureWorkspace();
        await using var session = new WorkspaceSession();
        WorkspaceSnapshot before = await session.GetCurrentAsync(fixture.SolutionPath, ct);
        string moneyFile = fixture.PathOf("MyApp.Domain", "Money.cs");

        // Act — edit the file on disk, then re-acquire.
        EditOnDisk(moneyFile, content => content + "\n// external-edit-marker\n");
        WorkspaceSnapshot after = await session.GetCurrentAsync(fixture.SolutionPath, ct);

        // Assert — a fresh snapshot whose document reflects the edit.
        after.ShouldNotBeSameAs(before);
        string text = await DocumentTextAsync(after, moneyFile, ct);
        text.ShouldContain("external-edit-marker");

        // …folded in place, not reloaded: the generation holds, and only the edited file's project bumps —
        // the delta the incremental fragment store diffs. Money.cs is in Domain, so Web and Billing are still.
        after.Generation.ShouldBe(before.Generation);
        after.ProjectEditVersions[Domain].ShouldBeGreaterThan(before.ProjectEditVersions[Domain]);
        after.ProjectEditVersions[Web].ShouldBe(before.ProjectEditVersions[Web]);
        after.ProjectEditVersions[Billing].ShouldBe(before.ProjectEditVersions[Billing]);
    }

    [Fact]
    public async Task GetCurrentAsync_NoChange_ReturnsSameSnapshotAndReadsNothing()
    {
        // Arrange — load, then promote every document past the racy window with one warmup sweep, so the
        // measured sweep is a pure O(stat) no-op (the steady-state case).
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var fixture = new TempFixtureWorkspace();
        await using var session = new WorkspaceSession();
        WorkspaceSnapshot loaded = await session.GetCurrentAsync(fixture.SolutionPath, ct);
        BackdateAllDocuments(loaded);
        await session.GetCurrentAsync(fixture.SolutionPath, ct); // warmup: content-verifies + promotes
        long readsAfterWarmup = session.SweepContentReads;

        // Act — sweep twice more with disk untouched.
        WorkspaceSnapshot again = await session.GetCurrentAsync(fixture.SolutionPath, ct);
        WorkspaceSnapshot againTwice = await session.GetCurrentAsync(fixture.SolutionPath, ct);

        // Assert — same snapshot instance throughout, and zero additional content reads.
        again.ShouldBeSameAs(loaded);
        againTwice.ShouldBeSameAs(loaded);
        (session.SweepContentReads - readsAfterWarmup).ShouldBe(0);

        // …and the delta the store diffs is stable: the generation holds and no project version ever bumped
        // (a pure no-op lifecycle never edits a document, so every seeded counter is still 0).
        again.Generation.ShouldBe(loaded.Generation);
        again.ProjectEditVersions.Values.ShouldAllBe(version => version == 0);
    }

    [Fact]
    public async Task GetCurrentAsync_MtimeBumpedIdenticalContent_ReturnsSameSnapshot()
    {
        // Arrange
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var fixture = new TempFixtureWorkspace();
        await using var session = new WorkspaceSession();
        WorkspaceSnapshot before = await session.GetCurrentAsync(fixture.SolutionPath, ct);
        string moneyFile = fixture.PathOf("MyApp.Domain", "Money.cs");

        // Act — bump the mtime without changing the bytes (a ReSharper-style touch).
        File.SetLastWriteTimeUtc(moneyFile, DateTime.UtcNow.AddSeconds(2));
        WorkspaceSnapshot after = await session.GetCurrentAsync(fixture.SolutionPath, ct);

        // Assert — content-verified equal, so no new snapshot is minted.
        after.ShouldBeSameAs(before);
    }

    [Fact]
    public async Task GetCurrentAsync_KnownDocumentDeleted_TriggersSingleFullReload()
    {
        // Arrange
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var fixture = new TempFixtureWorkspace();
        await using var session = new WorkspaceSession();
        await session.GetCurrentAsync(fixture.SolutionPath, ct);
        long reloadsBefore = session.FullReloadCount;
        string deleted = fixture.PathOf("MyApp.Domain", "PricingStrategy.cs");

        // Act
        File.Delete(deleted);
        WorkspaceSnapshot after = await session.GetCurrentAsync(fixture.SolutionPath, ct);

        // Assert — exactly one reload, and the deleted document is gone from the fresh solution.
        (session.FullReloadCount - reloadsBefore).ShouldBe(1);
        after.Solution.GetDocumentIdsWithFilePath(Path.GetFullPath(deleted)).ShouldBeEmpty();
    }

    [Fact]
    public async Task GetCurrentAsync_NewSourceFileInCone_ReloadsAndSurfacesNewType()
    {
        // Arrange — the cone-scan case that exceeds a pure mtime sweep: an SDK-glob add touches no
        // MSBuild file, so only a directory scan can see it.
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var fixture = new TempFixtureWorkspace();
        await using var session = new WorkspaceSession();
        await session.GetCurrentAsync(fixture.SolutionPath, ct);
        long reloadsBefore = session.FullReloadCount;
        string newFile = fixture.PathOf("MyApp.Domain", "NewlyAddedType.cs");

        // Act
        await File.WriteAllTextAsync(newFile, "namespace MyApp.Domain;\npublic class NewlyAddedType { }\n", ct);
        WorkspaceSnapshot after = await session.GetCurrentAsync(fixture.SolutionPath, ct);

        // Assert — one reload, and the new type is present in the extracted model.
        (session.FullReloadCount - reloadsBefore).ShouldBe(1);
        CodebaseModel model = await CodebaseExtractor.ExtractFromSolutionAsync(after.Solution, ct: ct);
        model.Types.ShouldContain(t => t.FullName == "MyApp.Domain.NewlyAddedType");
    }

    [Fact]
    public async Task GetCurrentAsync_ExcludedStrayInCone_NeverReloadsAndStaysOutOfModel()
    {
        // Arrange — MyApp.Domain carries a <Compile Remove>'d Snippets/*.cs: it lives in the project cone on
        // disk but is never compiled. Before the fix the cone scan compared the disk against the COMPILED
        // document set, so this stray read as a perpetual add and forced a full reload on every single call.
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var fixture = new TempFixtureWorkspace();
        await using var session = new WorkspaceSession();
        await session.GetCurrentAsync(fixture.SolutionPath, ct);
        long reloadsBefore = session.FullReloadCount;
        File.Exists(fixture.PathOf("MyApp.Domain", "Snippets", "ExcludedScratch.cs")).ShouldBeTrue();

        // Act — two more reconcile sweeps with disk untouched.
        await session.GetCurrentAsync(fixture.SolutionPath, ct);
        WorkspaceSnapshot after = await session.GetCurrentAsync(fixture.SolutionPath, ct);

        // Assert — the stray is recorded cone membership, so it never trips the scan (zero reloads) and never
        // enters the extracted model — the two halves of "a removed file is not a compiled document".
        (session.FullReloadCount - reloadsBefore).ShouldBe(0);
        CodebaseModel model = await CodebaseExtractor.ExtractFromSolutionAsync(after.Solution, ct: ct);
        model.Types.ShouldNotContain(t => t.FullName == "MyApp.Domain.Snippets.ExcludedScratchTypeMustNeverAppearInTheModel");
    }

    [Fact]
    public async Task GetCurrentAsync_CsprojTouched_TriggersReload()
    {
        // Arrange
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var fixture = new TempFixtureWorkspace();
        await using var session = new WorkspaceSession();
        WorkspaceSnapshot before = await session.GetCurrentAsync(fixture.SolutionPath, ct);
        long reloadsBefore = session.FullReloadCount;

        // Act — a structural touch on a project file.
        File.SetLastWriteTimeUtc(fixture.PathOf("MyApp.Domain", "MyApp.Domain.csproj"), DateTime.UtcNow.AddSeconds(2));
        WorkspaceSnapshot after = await session.GetCurrentAsync(fixture.SolutionPath, ct);

        // Assert — a structural reload, which bumps the generation so the store flushes and re-walks all.
        (session.FullReloadCount - reloadsBefore).ShouldBe(1);
        after.Generation.ShouldBeGreaterThan(before.Generation);
    }

    [Fact]
    public async Task GetCurrentAsync_ProjectAssetsTouched_TriggersReload()
    {
        // Arrange
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var fixture = new TempFixtureWorkspace();
        await using var session = new WorkspaceSession();
        await session.GetCurrentAsync(fixture.SolutionPath, ct);
        long reloadsBefore = session.FullReloadCount;

        // Act — a restore signal: obj/project.assets.json changes.
        File.SetLastWriteTimeUtc(
            fixture.PathOf("MyApp.Domain", "obj", "project.assets.json"), DateTime.UtcNow.AddSeconds(2));
        await session.GetCurrentAsync(fixture.SolutionPath, ct);

        // Assert
        (session.FullReloadCount - reloadsBefore).ShouldBe(1);
    }

    [Fact]
    public async Task GetCurrentAsync_DirectoryBuildPropsAppearsInAncestor_TriggersReload()
    {
        // Arrange
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var fixture = new TempFixtureWorkspace();
        await using var session = new WorkspaceSession();
        await session.GetCurrentAsync(fixture.SolutionPath, ct);
        long reloadsBefore = session.FullReloadCount;
        string solutionDirectory = Path.GetDirectoryName(fixture.SolutionPath)!;

        // Act — a props file that did not exist at load appears in the solution directory (a probe-chain
        // ancestor recorded as absent).
        await File.WriteAllTextAsync(Path.Combine(solutionDirectory, "Directory.Build.props"), "<Project />\n", ct);
        await session.GetCurrentAsync(fixture.SolutionPath, ct);

        // Assert
        (session.FullReloadCount - reloadsBefore).ShouldBe(1);
    }

    [Fact]
    public async Task GetCurrentAsync_TenConcurrentCallers_AllSucceedWithSingleLoad()
    {
        // Arrange — fire the very first (loading) call ten times at once.
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var fixture = new TempFixtureWorkspace();
        await using var session = new WorkspaceSession();

        // Act
        var calls = Enumerable.Range(0, 10)
            .Select(_ => session.GetCurrentAsync(fixture.SolutionPath, ct))
            .ToArray();
        var snapshots = await Task.WhenAll(calls);

        // Assert — all callers share one immutable snapshot, and the gate collapsed the burst to one load.
        snapshots.ShouldAllBe(s => ReferenceEquals(s, snapshots[0]));
        session.FullReloadCount.ShouldBe(1);
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_IsIdempotent()
    {
        // Arrange
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var fixture = new TempFixtureWorkspace();
        var session = new WorkspaceSession();
        await session.GetCurrentAsync(fixture.SolutionPath, ct);

        // Act
        await session.DisposeAsync();

        // Assert — a second dispose is a no-op, and post-dispose access is a clean ObjectDisposedException.
        await Should.NotThrowAsync(async () => await session.DisposeAsync());
        await Should.ThrowAsync<ObjectDisposedException>(async () => await session.GetCurrentAsync(fixture.SolutionPath, ct));
    }

    [Fact]
    public async Task GetCurrentAsync_FailedLoadThenGoodPath_RecoversAndSucceeds()
    {
        // Arrange
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var fixture = new TempFixtureWorkspace();
        await using var session = new WorkspaceSession();
        string bogusSolution = Path.Combine(Path.GetDirectoryName(fixture.SolutionPath)!, "DoesNotExist.sln");

        // Act — a bad path throws; the session resets to unloaded so the next call retries cleanly.
        var threw = false;
        try
        {
            await session.GetCurrentAsync(bogusSolution, ct);
        }
        catch
        {
            threw = true;
        }

        WorkspaceSnapshot recovered = await session.GetCurrentAsync(fixture.SolutionPath, ct);

        // Assert
        threw.ShouldBeTrue();
        recovered.Solution.Projects.ShouldNotBeEmpty();
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────────────

    private static void EditOnDisk(string path, Func<string, string> transform)
    {
        string content = File.ReadAllText(path);
        File.WriteAllText(path, transform(content));
        // A future mtime guarantees the sweep sees a delta against the load-time fingerprint.
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(2));
    }

    private static void BackdateAllDocuments(WorkspaceSnapshot snapshot)
    {
        DateTime wellPast = DateTime.UtcNow.AddDays(-1);
        var paths = snapshot.Solution.Projects
            .SelectMany(p => p.Documents)
            .Select(d => d.FilePath)
            .OfType<string>()
            .Distinct();

        foreach (string path in paths)
            try
            {
                File.SetLastWriteTimeUtc(path, wellPast);
            }
            catch (IOException)
            {
                // Best-effort: a file we cannot re-stamp stays racy and simply re-reads, which the
                // assertion's warmup baseline already absorbs.
            }
    }

    private static async Task<string> DocumentTextAsync(WorkspaceSnapshot snapshot, string path, CancellationToken ct)
    {
        DocumentId documentId = snapshot.Solution.GetDocumentIdsWithFilePath(Path.GetFullPath(path)).First();
        SourceText text = await snapshot.Solution.GetDocument(documentId)!.GetTextAsync(ct);
        return text.ToString();
    }
}