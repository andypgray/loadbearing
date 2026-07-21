using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Tests.Extraction;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Roslyn;

/// <summary>
///     Workspace-tier tests for <see cref="SessionFragmentStore" /> — the warm server's session-scoped
///     incremental fragment store. Each test drives a real <see cref="WorkspaceSession" /> over
///     its own restored MyApp copy to obtain genuine <see cref="WorkspaceSnapshot" />s (generation + edit
///     versions), then feeds them to the store and asserts on which projects it re-walked and on model
///     equivalence — never on wall time. The MyApp reference graph is Domain → Web → Billing (Domain
///     references Web, Web references Billing, Billing references nothing), so a Web edit's reverse-dependent
///     closure is {Web, Domain} and Billing is reused. Serialized with the other workspace-loading suites.
/// </summary>
[Collection("Serial")]
public sealed class SessionFragmentStoreTests
{
    private const string Domain = "MyApp.Domain";
    private const string Web = "MyApp.Web";
    private const string Billing = "MyApp.Legacy.Billing";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetFragmentsAsync_FirstCallThenSteadyState_ExtractsAllThenReusesAll()
    {
        // Arrange
        using var fixture = new TempFixtureWorkspace();
        await using var session = new WorkspaceSession();
        var store = new SessionFragmentStore();

        // Act 1 — the first call has nothing cached, so it flushes and walks every C# project.
        WorkspaceSnapshot snap1 = await session.GetCurrentAsync(fixture.SolutionPath, Ct);
        SessionFragmentSet first = await store.GetFragmentsAsync(snap1, Ct);

        // Assert 1 — all three re-extracted, and the merged model is byte-identical to a cold full extraction.
        first.ReExtractedProjects.ShouldBe([Billing, Domain, Web], true);
        store.LastReExtractedProjects.ShouldBe([Billing, Domain, Web], true);
        store.FullWalkCount.ShouldBe(1);

        CodebaseModel coldModel = await CodebaseExtractor.ExtractFromSolutionAsync(snap1.Solution, ct: Ct);
        ModelDump.Render(FragmentMerger.Merge(first.Fragments)).ShouldBe(ModelDump.Render(coldModel));

        // Act 2 — a second call with disk untouched must reuse everything.
        WorkspaceSnapshot snap2 = await session.GetCurrentAsync(fixture.SolutionPath, Ct);
        SessionFragmentSet second = await store.GetFragmentsAsync(snap2, Ct);

        // Assert 2 — zero re-extraction, no extra full walk, and the reused fragments merge to the same model.
        second.ReExtractedProjects.ShouldBeEmpty();
        store.LastReExtractedProjects.ShouldBeEmpty();
        store.FullWalkCount.ShouldBe(1);
        ModelDump.Render(FragmentMerger.Merge(second.Fragments)).ShouldBe(ModelDump.Render(coldModel));
    }

    [Fact]
    public async Task GetFragmentsAsync_SourceEditedInOneProject_ReExtractsDirtyPlusDependents()
    {
        // Arrange — populate the store from the clean tree.
        using var fixture = new TempFixtureWorkspace();
        await using var session = new WorkspaceSession();
        var store = new SessionFragmentStore();
        WorkspaceSnapshot snap1 = await session.GetCurrentAsync(fixture.SolutionPath, Ct);
        await store.GetFragmentsAsync(snap1, Ct);

        // Act — append a new type to a Web source file (a content edit the sweep folds in place), then re-get.
        string webFile = fixture.PathOf(Web, "WebTextExtensions.cs");
        EditOnDisk(webFile, content => content + "\npublic class WebIncrementalProbe { }\n");
        WorkspaceSnapshot snap2 = await session.GetCurrentAsync(fixture.SolutionPath, Ct);
        SessionFragmentSet edited = await store.GetFragmentsAsync(snap2, Ct);

        // Assert — re-extraction is EXACTLY Web (content-dirty) ∪ Domain (its only reverse-dependent); Billing,
        // which Web references but which references nothing, stays clean and is reused. No new full walk.
        edited.ReExtractedProjects.ShouldBe([Web, Domain], true);
        store.LastReExtractedProjects.ShouldBe([Web, Domain], true);
        store.FullWalkCount.ShouldBe(1);

        // The merged model carries the new type and equals a fresh cold extraction of the edited tree.
        CodebaseModel storeModel = FragmentMerger.Merge(edited.Fragments);
        CodebaseModel coldModel = await CodebaseExtractor.ExtractFromSolutionAsync(snap2.Solution, ct: Ct);
        ModelDump.Render(storeModel).ShouldContain("MyApp.Web.WebIncrementalProbe");
        ModelDump.Render(storeModel).ShouldBe(ModelDump.Render(coldModel));
    }

    [Fact]
    public async Task GetFragmentsAsync_CsprojTouched_FlushesAndFullReWalks()
    {
        // Arrange
        using var fixture = new TempFixtureWorkspace();
        await using var session = new WorkspaceSession();
        var store = new SessionFragmentStore();
        WorkspaceSnapshot snap1 = await session.GetCurrentAsync(fixture.SolutionPath, Ct);
        await store.GetFragmentsAsync(snap1, Ct);
        long walksBefore = store.FullWalkCount;

        // Act — a structural touch forces a full session reload (new generation), the store's flush signal.
        File.SetLastWriteTimeUtc(fixture.PathOf(Domain, "MyApp.Domain.csproj"), DateTime.UtcNow.AddSeconds(2));
        WorkspaceSnapshot snap2 = await session.GetCurrentAsync(fixture.SolutionPath, Ct);
        SessionFragmentSet reloaded = await store.GetFragmentsAsync(snap2, Ct);

        // Assert — the generation moved, so the store flushed and re-walked every project via the reload path.
        snap2.Generation.ShouldBeGreaterThan(snap1.Generation);
        reloaded.ReExtractedProjects.ShouldBe([Billing, Domain, Web], true);
        store.FullWalkCount.ShouldBe(walksBefore + 1);
    }

    [Fact]
    public async Task Merge_StoreFragmentsWithExcludedProject_MatchesColdExcludedExtraction()
    {
        // Arrange — the store always holds fragments for every project (spec included); a tool drops its own
        // at merge time. This pins that the merge-time drop is byte-identical to never extracting the project.
        using var fixture = new TempFixtureWorkspace();
        await using var session = new WorkspaceSession();
        var store = new SessionFragmentStore();
        WorkspaceSnapshot snapshot = await session.GetCurrentAsync(fixture.SolutionPath, Ct);
        SessionFragmentSet all = await store.GetFragmentsAsync(snapshot, Ct);

        // Act — merge the store's fragments minus Billing (as CodebaseSource.Retain does) versus a cold
        // extraction that excludes Billing at the input stage.
        var retained = all.Fragments.Where(fragment => fragment.ProjectName != Billing).ToList();
        CodebaseModel mergedExcluded = FragmentMerger.Merge(retained);
        CodebaseModel coldExcluded = await CodebaseExtractor.ExtractFromSolutionAsync(snapshot.Solution, [Billing], Ct);

        // Assert — dropping a referenced project at merge time (Billing survives as an external of Web) matches
        // never extracting it, so one store serves every tool whatever project each excludes.
        ModelDump.Render(mergedExcluded).ShouldBe(ModelDump.Render(coldExcluded));
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────────────

    private static void EditOnDisk(string path, Func<string, string> transform)
    {
        string content = File.ReadAllText(path);
        File.WriteAllText(path, transform(content));
        // A future mtime guarantees the sweep sees a delta against the load-time fingerprint.
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(2));
    }
}