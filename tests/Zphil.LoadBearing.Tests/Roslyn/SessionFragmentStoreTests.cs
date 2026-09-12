using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Caching;
using Zphil.LoadBearing.Roslyn.Extraction;
using Zphil.LoadBearing.Tests.Extraction;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Roslyn;

/// <summary>
///     Workspace-tier tests for <see cref="SessionFragmentStore" /> — the warm server's session-scoped
///     incremental fragment store. Each test drives a real <see cref="WorkspaceSession" />, taken from the
///     <see cref="WarmWorkspacePool" />, over its own restored MyApp copy to obtain genuine
///     <see cref="WorkspaceSnapshot" />s (generation + edit versions), then feeds them to the store and
///     asserts on which projects it re-walked and on model equivalence — never on wall time. The MyApp
///     reference graph is Domain → Web → Billing (Domain references Web, Web references Billing, Billing
///     references nothing), so a Web edit's reverse-dependent closure is {Web, Domain} and Billing is reused.
///     Serialized with the other workspace-loading suites.
/// </summary>
/// <remarks>
///     Nothing here needs a cold session, which is why the pool serves them all: a store starts with nothing
///     extracted, so its first call full-walks whatever absolute generation the session is at, and every
///     assertion is either on the store's own counters or on a relative generation comparison between two
///     snapshots of one test.
/// </remarks>
[Collection("Serial")]
public sealed class SessionFragmentStoreTests
{
    private const string Domain = "MyApp.Domain";
    private const string Web = "MyApp.Web";
    private const string Billing = "MyApp.Legacy.Billing";
    private const string MultiTfmCore = "MultiTfm.Core";
    private const string MultiTfmWeb = "MultiTfm.Web";

    [Fact]
    public async Task GetFragmentsAsync_FirstCallThenSteadyState_ExtractsAllThenReusesAll()
    {
        // Arrange
        using var fixture = new TempFixtureWorkspace();
        using var store = new SessionFragmentStore();

        // Act 1 — the first call has nothing cached, so it flushes and walks every C# project.
        WorkspaceSnapshot snap1 = await WarmWorkspacePool.GetCurrentAsync(fixture.SolutionPath, Ct);
        SessionFragmentSet first = await store.GetFragmentsAsync(snap1, null, Ct);

        // Assert 1 — all three re-extracted, and the merged model is byte-identical to a cold full extraction.
        first.ReExtractedProjects.ShouldBe([Billing, Domain, Web], true);
        store.LastReExtractedProjects.ShouldBe([Billing, Domain, Web], true);
        store.FullWalkCount.ShouldBe(1);

        CodebaseModel coldModel = await CodebaseExtractor.ExtractFromSolutionAsync(snap1.Solution, ct: Ct);
        FragmentMerger.Merge(first.Fragments)
            .ShouldModelTheSameAs(coldModel);

        // Act 2 — a second call with disk untouched must reuse everything.
        WorkspaceSnapshot snap2 = await WarmWorkspacePool.GetCurrentAsync(fixture.SolutionPath, Ct);
        SessionFragmentSet second = await store.GetFragmentsAsync(snap2, null, Ct);

        // Assert 2 — zero re-extraction, no extra full walk, and the reused fragments merge to the same model.
        second.ReExtractedProjects.ShouldBeEmpty();
        store.LastReExtractedProjects.ShouldBeEmpty();
        store.FullWalkCount.ShouldBe(1);
        FragmentMerger.Merge(second.Fragments)
            .ShouldModelTheSameAs(coldModel);
    }

    [Fact]
    public async Task GetFragmentsAsync_SourceEditedInOneProject_ReExtractsDirtyPlusDependents()
    {
        // Arrange — populate the store from the clean tree.
        using var fixture = new TempFixtureWorkspace();
        using var store = new SessionFragmentStore();
        WorkspaceSnapshot snap1 = await WarmWorkspacePool.GetCurrentAsync(fixture.SolutionPath, Ct);
        await store.GetFragmentsAsync(snap1, null, Ct);

        // Act — append a new type to a Web source file (a content edit the sweep folds in place), then re-get.
        string webFile = fixture.PathOf(Web, "WebTextExtensions.cs");
        FixtureEdits.EditOnDisk(webFile, content => content + "\npublic class WebIncrementalProbe { }\n");
        WorkspaceSnapshot snap2 = await WarmWorkspacePool.GetCurrentAsync(fixture.SolutionPath, Ct);
        SessionFragmentSet edited = await store.GetFragmentsAsync(snap2, null, Ct);

        // Assert — re-extraction is EXACTLY Web (content-dirty) ∪ Domain (its only reverse-dependent); Billing,
        // which Web references but which references nothing, stays clean and is reused. No new full walk.
        edited.ReExtractedProjects.ShouldBe([Web, Domain], true);
        store.LastReExtractedProjects.ShouldBe([Web, Domain], true);
        store.FullWalkCount.ShouldBe(1);

        // The merged model carries the new type and equals a fresh cold extraction of the edited tree.
        CodebaseModel storeModel = FragmentMerger.Merge(edited.Fragments);
        CodebaseModel coldModel = await CodebaseExtractor.ExtractFromSolutionAsync(snap2.Solution, ct: Ct);
        ModelDump.Render(storeModel)
            .ShouldContain("MyApp.Web.WebIncrementalProbe");
        storeModel.ShouldModelTheSameAs(coldModel);
    }

    [Fact]
    public async Task GetFragmentsAsync_CsprojTouched_FlushesAndFullReWalks()
    {
        // Arrange
        using var fixture = new TempFixtureWorkspace();
        using var store = new SessionFragmentStore();
        WorkspaceSnapshot snap1 = await WarmWorkspacePool.GetCurrentAsync(fixture.SolutionPath, Ct);
        await store.GetFragmentsAsync(snap1, null, Ct);
        long walksBefore = store.FullWalkCount;

        // Act — a structural touch forces a full session reload (new generation), the store's flush signal.
        File.SetLastWriteTimeUtc(fixture.PathOf(Domain, "MyApp.Domain.csproj"), DateTime.UtcNow.AddSeconds(2));
        WorkspaceSnapshot snap2 = await WarmWorkspacePool.GetCurrentAsync(fixture.SolutionPath, Ct);
        SessionFragmentSet reloaded = await store.GetFragmentsAsync(snap2, null, Ct);

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
        using var store = new SessionFragmentStore();
        WorkspaceSnapshot snapshot = await WarmWorkspacePool.GetCurrentAsync(fixture.SolutionPath, Ct);
        SessionFragmentSet all = await store.GetFragmentsAsync(snapshot, null, Ct);

        // Act — merge the store's fragments minus Billing (as CodebaseSource.Retain does) versus a cold
        // extraction that excludes Billing at the input stage.
        List<CodebaseFragment> retained = all.Fragments.Where(fragment => fragment.ProjectName != Billing)
            .ToList();
        CodebaseModel mergedExcluded = FragmentMerger.Merge(retained);
        CodebaseModel coldExcluded = await CodebaseExtractor.ExtractFromSolutionAsync(
            snapshot.Solution, [Billing], snapshot.TargetFrameworks, null, Ct);

        // Assert — dropping a referenced project at merge time (Billing survives as an external of Web) matches
        // never extracting it, so one store serves every tool whatever project each excludes.
        mergedExcluded.ShouldModelTheSameAs(coldExcluded);
    }

    [Fact]
    public async Task GetFragmentsAsync_MultiTargetedProjectEdited_ReExtractsBothFrameworksUnderOneName()
    {
        // Arrange — the MultiTfm fixture, whose Core csproj yields two Projects under one name and whose Web
        // project is its single reverse-dependent. Three separately-keyed pieces of the store meet on that
        // one name — the edit-version dirty set, the per-name fragment eviction, and the includeProjects
        // filter re-extraction passes — so a project spelled two ways anywhere would strand a stale fragment.
        using var fixture = new TempFixtureWorkspace("TestSolutions/MultiTfm", "MultiTfm.sln");
        using var store = new SessionFragmentStore();
        WorkspaceSnapshot clean = await WarmWorkspacePool.GetCurrentAsync(fixture.SolutionPath, Ct);
        SessionFragmentSet first = await store.GetFragmentsAsync(clean, null, Ct);

        // Both frameworks' fragments are held under the one project name from the start.
        first.Fragments.Where(fragment => fragment.ProjectName == MultiTfmCore)
            .Select(fragment => fragment.TargetFramework)
            .ShouldBe(["net10.0", "netstandard2.0"]);

        // Act — an edit to a file both frameworks compile.
        string widget = fixture.PathOf(MultiTfmCore, "Widget.cs");
        FixtureEdits.EditOnDisk(
            widget,
            content => content + "\nnamespace MultiTfm.Core\n{\n    public class WidgetProbe\n    {\n    }\n}\n");
        WorkspaceSnapshot edited = await WarmWorkspacePool.GetCurrentAsync(fixture.SolutionPath, Ct);
        SessionFragmentSet reExtracted = await store.GetFragmentsAsync(edited, null, Ct);

        // Assert — one name went dirty (plus its reverse-dependent), and BOTH of its fragments came back,
        // each still carrying its own framework, with the new type in the merged model.
        reExtracted.ReExtractedProjects.ShouldBe([MultiTfmCore, MultiTfmWeb], true);
        store.FullWalkCount.ShouldBe(1);
        reExtracted.Fragments.Where(fragment => fragment.ProjectName == MultiTfmCore)
            .Select(fragment => fragment.TargetFramework)
            .ShouldBe(["net10.0", "netstandard2.0"]);
        ModelDump.Render(FragmentMerger.Merge(reExtracted.Fragments))
            .ShouldContain("MultiTfm.Core.WidgetProbe");
    }

    [Fact]
    public async Task GetFragmentsAsync_CommentOnlyEditInOneProject_RemapsSitesWithoutReWalking()
    {
        // Arrange — populate the store from the clean tree, and record where Web's sites sat.
        using var fixture = new TempFixtureWorkspace();
        using var store = new SessionFragmentStore();
        WorkspaceSnapshot snap1 = await WarmWorkspacePool.GetCurrentAsync(fixture.SolutionPath, Ct);
        SessionFragmentSet clean = await store.GetFragmentsAsync(snap1, null, Ct);
        List<FragmentSite> sitesBefore = WebEdgeSites(clean);

        // Act — two comment lines at the top of a Web file: no fact changes, and every site below them moves
        // down exactly two lines.
        string homeController = fixture.PathOf(Web, "HomeController.cs");
        FixtureEdits.EditOnDisk(homeController, content => "// probe one\n// probe two\n" + content);
        WorkspaceSnapshot snap2 = await WarmWorkspacePool.GetCurrentAsync(fixture.SolutionPath, Ct);
        SessionFragmentSet edited = await store.GetFragmentsAsync(snap2, null, Ct);

        // Assert — nothing was re-walked. Web's stored fragments were moved instead, and Domain, its only
        // reverse-dependent, was left alone: a dependent's own sites are in its own files.
        edited.ReExtractedProjects.ShouldBeEmpty();
        store.LastReExtractedProjects.ShouldBeEmpty();
        store.LastRemappedProjects.ShouldBe([Web]);
        store.FullWalkCount.ShouldBe(1);

        CodebaseModel coldModel = await CodebaseExtractor.ExtractFromSolutionAsync(snap2.Solution, ct: Ct);
        FragmentMerger.Merge(edited.Fragments)
            .ShouldModelTheSameAs(coldModel);

        // …and the moved sites themselves, which the model comparison establishes but does not show: the same
        // files, and every line inside the edited file two lower than it was.
        List<FragmentSite> sitesAfter = WebEdgeSites(edited);
        List<int> linesBefore = LinesIn(sitesBefore, homeController);
        List<int> linesAfter = LinesIn(sitesAfter, homeController);
        linesBefore.ShouldNotBeEmpty();
        linesAfter.ShouldBe(linesBefore.Select(line => line + 2));
        FilesOf(sitesAfter)
            .ShouldBe(FilesOf(sitesBefore));
    }

    [Fact]
    public async Task GetFragmentsAsync_StringLiteralEdit_ReWalksDirtyPlusDependents()
    {
        // A token's text changes and nothing moves line. The shape digest reads token text, so this is the
        // cheapest edit that is still not trivia.
        await ShouldReWalkWebAndDomain(content => content.Replace("\"total requested\"", "\"total requested!\""));
    }

    [Fact]
    public async Task GetFragmentsAsync_PreprocessorDirectiveAdded_ReWalks()
    {
        // Wrap a method in an always-true conditional. Every token survives and the code stays active, but a
        // directive is trivia the shape digest deliberately reads, so this re-walks rather than remapping —
        // conservative here, and never wrong.
        await ShouldReWalkWebAndDomain(WrapExportStampInConditional);
    }

    [Fact]
    public async Task GetFragmentsAsync_GeneratedBannerInserted_ReWalks()
    {
        // A comment, and so pure trivia by the token test alone; but it is the one comment a fact reads, so
        // it enters the digest as its verdict and the file's types flip to generated. The cold model is the
        // oracle that the flipped flag really did arrive: a remap would have kept the old one while moving
        // every line correctly.
        await ShouldReWalkWebAndDomain(content => "// <auto-generated />\n" + content);
    }

    [Fact]
    public async Task GetFragmentsAsync_TokenBearingLineSplit_ReWalks()
    {
        // Chop one statement across two lines. The shapes stay equal (only whitespace moved), so this is the
        // fallback the line map itself refuses rather than the digest: a split line's sites cannot be moved
        // by line alone.
        await ShouldReWalkWebAndDomain(content => content.Replace(
            "        log.Append(description);", "        log\n            .Append(description);"));
    }

    [Fact]
    public async Task GetFragmentsAsync_TriviaThenRealThenTrivia_ClassifiesEachCallAgainstTheLastWalk()
    {
        // Arrange — each call classifies against the text the STORED fragments describe, not against the load,
        // so a remap has to move that reference forward or the second trivia edit would be measured twice.
        using var fixture = new TempFixtureWorkspace();
        using var store = new SessionFragmentStore();
        WorkspaceSnapshot snap1 = await WarmWorkspacePool.GetCurrentAsync(fixture.SolutionPath, Ct);
        await store.GetFragmentsAsync(snap1, null, Ct);
        string homeController = fixture.PathOf(Web, "HomeController.cs");

        // Act/Assert 1 — a comment edit remaps Web.
        FixtureEdits.EditOnDisk(homeController, content => "// probe one\n" + content);
        WorkspaceSnapshot snap2 = await WarmWorkspacePool.GetCurrentAsync(fixture.SolutionPath, Ct);
        SessionFragmentSet first = await store.GetFragmentsAsync(snap2, null, Ct);
        first.ReExtractedProjects.ShouldBeEmpty();
        store.LastRemappedProjects.ShouldBe([Web]);
        CodebaseModel firstCold = await CodebaseExtractor.ExtractFromSolutionAsync(snap2.Solution, ct: Ct);
        FragmentMerger.Merge(first.Fragments)
            .ShouldModelTheSameAs(firstCold);

        // Act/Assert 2 — a second comment edit remaps again, against the sites the first one left behind.
        FixtureEdits.EditOnDisk(homeController, content => "// probe two\n" + content);
        WorkspaceSnapshot snap3 = await WarmWorkspacePool.GetCurrentAsync(fixture.SolutionPath, Ct);
        SessionFragmentSet second = await store.GetFragmentsAsync(snap3, null, Ct);
        second.ReExtractedProjects.ShouldBeEmpty();
        store.LastRemappedProjects.ShouldBe([Web]);
        CodebaseModel secondCold = await CodebaseExtractor.ExtractFromSolutionAsync(snap3.Solution, ct: Ct);
        FragmentMerger.Merge(second.Fragments)
            .ShouldModelTheSameAs(secondCold);

        // Act/Assert 3 — a real edit re-walks Web and its dependent, and remaps nothing.
        FixtureEdits.EditOnDisk(
            homeController, content => content.Replace("\"total requested\"", "\"total requested!\""));
        WorkspaceSnapshot snap4 = await WarmWorkspacePool.GetCurrentAsync(fixture.SolutionPath, Ct);
        SessionFragmentSet third = await store.GetFragmentsAsync(snap4, null, Ct);
        third.ReExtractedProjects.ShouldBe([Web, Domain], true);
        store.LastRemappedProjects.ShouldBeEmpty();
        CodebaseModel thirdCold = await CodebaseExtractor.ExtractFromSolutionAsync(snap4.Solution, ct: Ct);
        FragmentMerger.Merge(third.Fragments)
            .ShouldModelTheSameAs(thirdCold);

        // Act/Assert 4 — and a comment edit after the re-walk remaps once more.
        FixtureEdits.EditOnDisk(homeController, content => "// probe three\n" + content);
        WorkspaceSnapshot snap5 = await WarmWorkspacePool.GetCurrentAsync(fixture.SolutionPath, Ct);
        SessionFragmentSet fourth = await store.GetFragmentsAsync(snap5, null, Ct);
        fourth.ReExtractedProjects.ShouldBeEmpty();
        store.LastRemappedProjects.ShouldBe([Web]);
        store.FullWalkCount.ShouldBe(1);
        CodebaseModel fourthCold = await CodebaseExtractor.ExtractFromSolutionAsync(snap5.Solution, ct: Ct);
        FragmentMerger.Merge(fourth.Fragments)
            .ShouldModelTheSameAs(fourthCold);
    }

    [Fact]
    public async Task GetFragmentsAsync_MultiTargetedProjectCommentEdit_RemapsBothFrameworksAndLeavesWebAlone()
    {
        // Arrange — one name, two compilations, and one file both of them compile: the maps the two
        // frameworks produce for that file have to agree before either fragment may be moved.
        using var fixture = new TempFixtureWorkspace("TestSolutions/MultiTfm", "MultiTfm.sln");
        using var store = new SessionFragmentStore();
        WorkspaceSnapshot clean = await WarmWorkspacePool.GetCurrentAsync(fixture.SolutionPath, Ct);
        await store.GetFragmentsAsync(clean, null, Ct);

        // Act
        string widget = fixture.PathOf(MultiTfmCore, "Widget.cs");
        FixtureEdits.EditOnDisk(widget, content => "// probe one\n// probe two\n" + content);
        WorkspaceSnapshot edited = await WarmWorkspacePool.GetCurrentAsync(fixture.SolutionPath, Ct);
        SessionFragmentSet remapped = await store.GetFragmentsAsync(edited, null, Ct);

        // Assert — one name remapped, nothing re-walked, and BOTH of its fragments are still there under their
        // own frameworks: a remap that dropped or reordered one would strand a framework's facts.
        remapped.ReExtractedProjects.ShouldBeEmpty();
        store.LastReExtractedProjects.ShouldBeEmpty();
        store.LastRemappedProjects.ShouldBe([MultiTfmCore]);
        store.FullWalkCount.ShouldBe(1);
        remapped.Fragments.Where(fragment => fragment.ProjectName == MultiTfmCore)
            .Select(fragment => fragment.TargetFramework)
            .ShouldBe(["net10.0", "netstandard2.0"]);

        CodebaseModel coldModel = await CodebaseExtractor.ExtractFromSolutionAsync(
            edited.Solution, null, edited.TargetFrameworks, null, Ct);
        FragmentMerger.Merge(remapped.Fragments)
            .ShouldModelTheSameAs(coldModel);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────────────

    // The scene four of the rows above share, differing only in the edit they make: populate the store from
    // the clean tree, edit one Web source file, re-get. Four [Fact]s rather than a [Theory] because they are
    // four distinct facts about what reaches a document's shape, and theory data carrying source text lands
    // in every display name.
    private static async Task ShouldReWalkWebAndDomain(Func<string, string> edit)
    {
        // Arrange
        using var fixture = new TempFixtureWorkspace();
        using var store = new SessionFragmentStore();
        WorkspaceSnapshot snap1 = await WarmWorkspacePool.GetCurrentAsync(fixture.SolutionPath, Ct);
        await store.GetFragmentsAsync(snap1, null, Ct);

        // Act
        FixtureEdits.EditOnDisk(fixture.PathOf(Web, "HomeController.cs"), edit);
        WorkspaceSnapshot snap2 = await WarmWorkspacePool.GetCurrentAsync(fixture.SolutionPath, Ct);
        SessionFragmentSet edited = await store.GetFragmentsAsync(snap2, null, Ct);

        // Assert — the shape changed, so Web and its reverse-dependent Domain were re-walked, nothing was
        // remapped, and no new full walk was needed.
        edited.ReExtractedProjects.ShouldBe([Web, Domain], true);
        store.LastReExtractedProjects.ShouldBe([Web, Domain], true);
        store.LastRemappedProjects.ShouldBeEmpty();
        store.FullWalkCount.ShouldBe(1);

        CodebaseModel coldModel = await CodebaseExtractor.ExtractFromSolutionAsync(snap2.Solution, ct: Ct);
        FragmentMerger.Merge(edited.Fragments)
            .ShouldModelTheSameAs(coldModel);
    }

    // Wraps HomeController's ExportStamp in an always-true conditional, anchored on the two method
    // declarations that bracket it so the edit is line-ending agnostic. Every token and every active line
    // survives; only the two directives are new.
    private static string WrapExportStampInConditional(string source)
    {
        return source.Replace(
                "    public System.DateTime ExportStamp()", "#if true\n    public System.DateTime ExportStamp()")
            .Replace(
                "    public System.DateTime ExportStampUtc()", "#endif\n    public System.DateTime ExportStampUtc()");
    }

    // Every site on every reference edge of the Web project's fragments, in the order the fragments hold
    // them — the store's stored positions, read without a merge in between.
    private static List<FragmentSite> WebEdgeSites(SessionFragmentSet set)
    {
        return set.Fragments.Where(fragment => fragment.ProjectName == Web)
            .SelectMany(fragment => fragment.Edges)
            .SelectMany(edge => edge.Sites)
            .ToList();
    }

    private static List<int> LinesIn(IEnumerable<FragmentSite> sites, string file)
    {
        return sites.Where(site => string.Equals(site.File, file, PathComparison.Comparison))
            .Select(site => site.Line)
            .ToList();
    }

    private static List<string> FilesOf(IEnumerable<FragmentSite> sites)
    {
        return sites.Select(site => site.File)
            .Distinct(PathComparison.Comparer)
            .Order(PathComparison.Comparer)
            .ToList();
    }
}
