using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     The integrity pin under <see cref="WarmWorkspacePool" />: a warm session that outlives the test which
///     loaded it must never serve the previous test's tree. Both facts drive the exact sequence the harness
///     performs between tests in a class — lease, mutate, read; release; lease again (which resets the tree
///     to pristine), read again — and assert the second read sees the fixture, not the mutation.
/// </summary>
/// <remarks>
///     The two facts split on how <c>WorkspaceSession</c> has to notice the reset. An in-place content
///     restore is folded into the snapshot chain; a deleted file forces a full reload. The first is the one
///     with a
///     <see cref="Zphil.LoadBearing.Roslyn.Caching.FileFreshness.RacyWindow">racy window</see>, so it gets
///     the load-count assertion too.
/// </remarks>
[Collection("Serial")]
public sealed class WarmWorkspacePoolTests
{
    private const string Domain = "MyApp.Domain";

    // A delegate nothing else in the fixture names, so renaming it changes the extracted model and breaks
    // no other file. The replacement is the SAME LENGTH deliberately: it strips the fingerprint's
    // length field of any discriminating power, leaving the reset visible on write time alone.
    private const string PristineType = "PricingStrategy";

    private const string MutatedType = "PricingStrateqy";

    private const string AddedType = "WarmPoolProbeType";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LeaseReset_AfterASessionFoldedASameLengthEdit_ServesPristineWithoutReloading()
    {
        // Arrange — lease the class's tree, rename the delegate in place, and let the pool see it. The
        // fingerprint the sweep records here is captured within the racy window of the write it just read,
        // so it stays unpromoted and the next sweep is obliged to content-verify rather than trust the stat.
        using (var mutated = new TempFixtureWorkspace())
        {
            string strategyFile = mutated.PathOf(Domain, $"{PristineType}.cs");
            await File.WriteAllTextAsync(
                strategyFile,
                (await File.ReadAllTextAsync(strategyFile, Ct)).Replace(PristineType, MutatedType, StringComparison.Ordinal),
                Ct);

            CodebaseModel model = await ExtractAsync(mutated.SolutionPath);
            model.Types.ShouldContain(type => type.FullName == $"{Domain}.{MutatedType}");
        }

        long loadsBeforeReset = WorkspaceLoader.LoadCount;

        // Act — the next lease of the same key resets the tree, exactly as the next test in a class would.
        using var restored = new TempFixtureWorkspace();
        CodebaseModel afterReset = await ExtractAsync(restored.SolutionPath);

        // Assert — the reset is visible: the fixture's type is back and the mutation is gone…
        afterReset.Types.ShouldContain(type => type.FullName == $"{Domain}.{PristineType}");
        afterReset.Types.ShouldNotContain(type => type.FullName == $"{Domain}.{MutatedType}");

        // …and it was reconciled, not reloaded — the same warm session served both leases, which is the
        // whole point of the pool. A non-zero delta here means the reset had to drop the session (a held
        // file), so the win is being paid for twice.
        (WorkspaceLoader.LoadCount - loadsBeforeReset).ShouldBe(0);
    }

    [Fact]
    public async Task LeaseReset_AfterASessionSawAnAddedFile_ServesTheTreeWithoutIt()
    {
        // Arrange — a file that did not exist at load: only the cone scan can see the add, and only a full
        // reload can absorb it. The reset then deletes it, which the sweep must also see.
        using (var added = new TempFixtureWorkspace())
        {
            await File.WriteAllTextAsync(
                added.PathOf(Domain, $"{AddedType}.cs"), $"namespace {Domain};\n\npublic class {AddedType};\n", Ct);

            CodebaseModel model = await ExtractAsync(added.SolutionPath);
            model.Types.ShouldContain(type => type.FullName == $"{Domain}.{AddedType}");
        }

        // Act
        using var swept = new TempFixtureWorkspace();
        CodebaseModel afterReset = await ExtractAsync(swept.SolutionPath);

        // Assert — the deleted document is gone from the reused session's solution.
        afterReset.Types.ShouldNotContain(type => type.FullName == $"{Domain}.{AddedType}");
    }

    private static async Task<CodebaseModel> ExtractAsync(string solutionPath)
    {
        WorkspaceSnapshot snapshot = await WarmWorkspacePool.GetCurrentAsync(solutionPath, Ct);
        return await CodebaseExtractor.ExtractFromSolutionAsync(snapshot.Solution, ct: Ct);
    }
}