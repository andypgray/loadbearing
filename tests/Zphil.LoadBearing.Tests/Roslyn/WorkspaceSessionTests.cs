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
///     host. Every case mutates disk between calls and asserts on what the next call does about it.
///     Serialized with the rest of the workspace-loading suites: each opens a real <c>MSBuildWorkspace</c>.
/// </summary>
/// <remarks>
///     Two shapes of test live here, and the difference decides which session a case gets. A test whose
///     subject is a <em>delta</em> — one reload, a bumped edit version, the same snapshot back — takes the
///     class's <see cref="SharedWorkspaceSession" />: it re-baselines the counters after its own first
///     acquisition, so a tree an earlier case edited cannot skew it, and the eight of them share one
///     MSBuild load instead of paying eight. A test whose subject is the <em>virgin</em> state — zero reads
///     ever, exactly one load ever, first-load concurrency, recovery from a failed first load — asserts
///     absolute counters and so must mint its own session over its own copy.
/// </remarks>
[Collection("Serial")]
public sealed class WorkspaceSessionTests(SharedWorkspaceSession shared) : IClassFixture<SharedWorkspaceSession>
{
    private const string Domain = "MyApp.Domain";
    private const string Web = "MyApp.Web";
    private const string Billing = "MyApp.Legacy.Billing";

    [Fact]
    public async Task GetCurrentAsync_DocumentEditedOnDisk_ReturnsRefreshedSnapshotWithEdit()
    {
        // Arrange
        CancellationToken ct = TestContext.Current.CancellationToken;
        WorkspaceSession session = shared.Session;
        WorkspaceSnapshot before = await session.GetCurrentAsync(shared.SolutionPath, ct);
        string moneyFile = shared.PathOf("MyApp.Domain", "Money.cs");

        // Act — edit the file on disk, then re-acquire.
        FixtureEdits.EditOnDisk(moneyFile, content => content + "\n// external-edit-marker\n");
        WorkspaceSnapshot after = await session.GetCurrentAsync(shared.SolutionPath, ct);

        // Assert — a fresh snapshot whose document reflects the edit.
        after.ShouldNotBeSameAs(before);
        string text = await DocumentTextAsync(after, moneyFile, ct);
        text.ShouldContain("external-edit-marker");

        // …folded in place, not reloaded: the generation holds, and only the edited file's project bumps —
        // the delta the incremental fragment store diffs. Money.cs is in Domain, so Web and Billing are still.
        after.Generation.ShouldBe(before.Generation);
        after.ProjectEditVersions[Domain]
            .ShouldBeGreaterThan(before.ProjectEditVersions[Domain]);
        after.ProjectEditVersions[Web]
            .ShouldBe(before.ProjectEditVersions[Web]);
        after.ProjectEditVersions[Billing]
            .ShouldBe(before.ProjectEditVersions[Billing]);
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
        // Best-effort: a file that cannot be re-stamped stays racy and simply re-reads, which the warmup
        // below absorbs into the baseline.
        FixtureEdits.BackdateAllDocuments(loaded);
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
        WorkspaceSession session = shared.Session;
        WorkspaceSnapshot before = await session.GetCurrentAsync(shared.SolutionPath, ct);
        string moneyFile = shared.PathOf("MyApp.Domain", "Money.cs");

        // Act — bump the mtime without changing the bytes (an IDE-style touch).
        File.SetLastWriteTimeUtc(moneyFile, DateTime.UtcNow.AddSeconds(2));
        WorkspaceSnapshot after = await session.GetCurrentAsync(shared.SolutionPath, ct);

        // Assert — content-verified equal, so no new snapshot is minted.
        after.ShouldBeSameAs(before);
    }

    [Fact]
    public async Task GetCurrentAsync_KnownDocumentDeleted_TriggersSingleFullReload()
    {
        // Arrange
        CancellationToken ct = TestContext.Current.CancellationToken;
        WorkspaceSession session = shared.Session;
        await session.GetCurrentAsync(shared.SolutionPath, ct);
        long reloadsBefore = session.FullReloadCount;
        string deleted = shared.PathOf("MyApp.Domain", "PricingStrategy.cs");

        // Act
        File.Delete(deleted);
        WorkspaceSnapshot after = await session.GetCurrentAsync(shared.SolutionPath, ct);

        // Assert — exactly one reload, and the deleted document is gone from the fresh solution.
        (session.FullReloadCount - reloadsBefore).ShouldBe(1);
        after.Solution.GetDocumentIdsWithFilePath(Path.GetFullPath(deleted))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task GetCurrentAsync_NewSourceFileInCone_ReloadsAndSurfacesNewType()
    {
        // Arrange — the cone-scan case that exceeds a pure mtime sweep: an SDK-glob add touches no
        // MSBuild file, so only a directory scan can see it.
        CancellationToken ct = TestContext.Current.CancellationToken;
        WorkspaceSession session = shared.Session;
        await session.GetCurrentAsync(shared.SolutionPath, ct);
        long reloadsBefore = session.FullReloadCount;
        string newFile = shared.PathOf("MyApp.Domain", "NewlyAddedType.cs");

        // Act
        await File.WriteAllTextAsync(newFile, "namespace MyApp.Domain;\npublic class NewlyAddedType { }\n", ct);
        WorkspaceSnapshot after = await session.GetCurrentAsync(shared.SolutionPath, ct);

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
        WorkspaceSession session = shared.Session;
        await session.GetCurrentAsync(shared.SolutionPath, ct);
        long reloadsBefore = session.FullReloadCount;
        File.Exists(shared.PathOf("MyApp.Domain", "Snippets", "ExcludedScratch.cs"))
            .ShouldBeTrue();

        // Act — two more reconcile sweeps with disk untouched.
        await session.GetCurrentAsync(shared.SolutionPath, ct);
        WorkspaceSnapshot after = await session.GetCurrentAsync(shared.SolutionPath, ct);

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
        WorkspaceSession session = shared.Session;
        WorkspaceSnapshot before = await session.GetCurrentAsync(shared.SolutionPath, ct);
        long reloadsBefore = session.FullReloadCount;

        // Act — a structural touch on a project file.
        File.SetLastWriteTimeUtc(shared.PathOf("MyApp.Domain", "MyApp.Domain.csproj"), DateTime.UtcNow.AddSeconds(2));
        WorkspaceSnapshot after = await session.GetCurrentAsync(shared.SolutionPath, ct);

        // Assert — a structural reload, which bumps the generation so the store flushes and re-walks all.
        (session.FullReloadCount - reloadsBefore).ShouldBe(1);
        after.Generation.ShouldBeGreaterThan(before.Generation);
    }

    [Fact]
    public async Task GetCurrentAsync_ProjectAssetsTouched_TriggersReload()
    {
        // Arrange
        CancellationToken ct = TestContext.Current.CancellationToken;
        WorkspaceSession session = shared.Session;
        await session.GetCurrentAsync(shared.SolutionPath, ct);
        long reloadsBefore = session.FullReloadCount;

        // Act — a restore signal: obj/project.assets.json changes.
        File.SetLastWriteTimeUtc(
            shared.PathOf("MyApp.Domain", "obj", "project.assets.json"), DateTime.UtcNow.AddSeconds(2));
        await session.GetCurrentAsync(shared.SolutionPath, ct);

        // Assert
        (session.FullReloadCount - reloadsBefore).ShouldBe(1);
    }

    [Fact]
    public async Task GetCurrentAsync_DirectoryBuildPropsAppearsInAncestor_TriggersReload()
    {
        // One representative probe rather than all of FileStamping.StructuralProbeFileNames, because every
        // case here loads a real workspace. The array is exercised whole, name by name, against the same
        // existence-flip rule by ExtractionCacheStoreTests.ReadAndValidate_NewStructuralProbeFileAppearsInAncestor_ReturnsMiss.

        // Arrange
        CancellationToken ct = TestContext.Current.CancellationToken;
        WorkspaceSession session = shared.Session;
        await session.GetCurrentAsync(shared.SolutionPath, ct);
        long reloadsBefore = session.FullReloadCount;
        string propsFile = Path.Combine(Path.GetDirectoryName(shared.SolutionPath)!, "Directory.Build.props");

        try
        {
            // Act — a props file that did not exist at load appears in the solution directory (a probe-chain
            // ancestor recorded as absent).
            await File.WriteAllTextAsync(propsFile, "<Project />\n", ct);
            await session.GetCurrentAsync(shared.SolutionPath, ct);

            // Assert
            (session.FullReloadCount - reloadsBefore).ShouldBe(1);
        }
        finally
        {
            // The write lands in an ancestor of the whole fixture copy, so it outlives this test's tree
            // unless it is taken back: every later case would build against a props file it never asked for.
            File.Delete(propsFile);
        }
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

    private static async Task<string> DocumentTextAsync(WorkspaceSnapshot snapshot, string path, CancellationToken ct)
    {
        DocumentId documentId = snapshot.Solution.GetDocumentIdsWithFilePath(Path.GetFullPath(path))
            .First();
        SourceText text = await snapshot.Solution.GetDocument(documentId)!.GetTextAsync(ct);
        return text.ToString();
    }
}

/// <summary>
///     One <see cref="WorkspaceSession" /> over one private copy of the MyApp fixture, shared by every
///     delta-measuring case in <see cref="WorkspaceSessionTests" />.
/// </summary>
/// <remarks>
///     <para>
///         The copy is <see cref="TempFixtureWorkspace.Dedicated" />, not the leased one: a session is bound
///         to the path it loaded, so the tree has to outlive the test that mutated it — and taking the class
///         lease here would push every case that mints its own session onto a private copy plus a restore.
///     </para>
///     <para>
///         Cases therefore inherit each other's edits, which is safe precisely because each one re-baselines
///         after its own first acquisition: that call absorbs whatever the previous case left on disk, and
///         the assertion that follows only ever reads the delta across the mutation under test.
///     </para>
/// </remarks>
public sealed class SharedWorkspaceSession : IAsyncDisposable
{
    private readonly TempFixtureWorkspace _fixture = TempFixtureWorkspace.Dedicated();

    /// <summary>The session every delta-measuring case reconciles through.</summary>
    public WorkspaceSession Session { get; } = new();

    /// <summary>The copied solution the session is bound to.</summary>
    public string SolutionPath => _fixture.SolutionPath;

    /// <summary>Disposes the session first, so the copy is unheld when it is deleted.</summary>
    public async ValueTask DisposeAsync()
    {
        await Session.DisposeAsync();
        _fixture.Dispose();
    }

    /// <summary>Absolute path to a file inside the copy, from solution-relative segments.</summary>
    public string PathOf(params string[] relativeSegments)
    {
        return _fixture.PathOf(relativeSegments);
    }
}
