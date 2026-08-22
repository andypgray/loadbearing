using Shouldly;
using Xunit;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Hosting;
using Zphil.LoadBearing.Tests.Mcp.TestDoubles;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     <c>explain</c> against the persisted extraction cache, over the one fixture that declares a spec
///     project (<see cref="LayoutAppFixture" />) and therefore the only bed where explain resolves by
///     convention against a real workspace — a prebuilt DLL <c>--spec</c> answers on the fast path and
///     never reaches a cache at all.
/// </summary>
/// <remarks>
///     <para>
///         Explain is the verb the cache does the most for and the only one that consumes a slot without
///         ever filling it. It never calls <c>ExtractAsync</c> — the model it dumps is the spec's — so it
///         reaches neither the fingerprint nor the write-back, and a miss leaves the root empty. What a hit
///         spares it is correspondingly the whole of its cost rather than a share of it: the recorded
///         resolution replays, the model loads from the DLL it names, and no design-time build runs.
///     </para>
///     <para>
///         Every run is by convention rather than <c>--spec</c>, because the recorded resolution is keyed on
///         the normalized spec argument: the filling run and the reading run have to spell it the same way
///         or the hit falls back to a cold resolve and the fact would pass for the wrong reason. In the
///         "Serial" collection with the rest of the workspace-loading suites.
///     </para>
/// </remarks>
[Collection("Serial")]
public sealed class ExplainCacheE2ETests
{
    private const string RuleId = LayoutAppFixture.RuleId;

    [Fact]
    public async Task Explain_OnASlotAnotherVerbFilled_AnswersWithNoWorkspaceAndIsByteIdentical()
    {
        using TempFixtureWorkspace workspace = BuiltLayoutCopy();
        using var cache = new TempCacheRoot("explain-cache");

        (CliResult cold, long coldLoads) = await ExplainAsync(workspace, cache.Root);

        cold.ShouldSucceed($"{RuleId} (enforce)");
        coldLoads.ShouldBe(1); // a convention spec resolves against the solution, so a miss opens one
        cache.HasCacheFile()
            .ShouldBeFalse(); // and explain, which never extracts, has nothing to write back

        // A verb that does extract fills the slot — and records this spec's resolution in it, which is what
        // the hit below replays instead of re-resolving against a workspace it no longer opens.
        (CliResult check, long checkLoads) = await RunAsync(cache.Root, "check", workspace.SolutionPath);
        check.ShouldSucceed($"pass {RuleId}");
        checkLoads.ShouldBe(1);
        cache.HasCacheFile()
            .ShouldBeTrue();

        (CliResult cached, long cachedLoads) = await ExplainAsync(workspace, cache.Root);

        cachedLoads.ShouldBe(0); // the whole of what a convention --spec costs, skipped
        cached.Out.ShouldBe(cold.Out);
        cached.Err.ShouldBe(cold.Err);
        cached.Exit.ShouldBe(cold.Exit);
    }

    [Fact]
    public async Task Explain_NoCache_StaysColdOverAFilledSlot()
    {
        using TempFixtureWorkspace workspace = BuiltLayoutCopy();
        using var cache = new TempCacheRoot("explain-cache");

        await RunAsync(cache.Root, "check", workspace.SolutionPath);

        (CliResult result, long loads) = await ExplainAsync(workspace, cache.Root, "--no-cache");

        result.ShouldSucceed($"{RuleId} (enforce)");
        loads.ShouldBe(1); // the flag is what keeps explain behaving as it always did
    }

    // ── harness ───────────────────────────────────────────────────────────────────────────────────────────

    private static Task<(CliResult Result, long Loads)> ExplainAsync(
        TempFixtureWorkspace workspace, string cacheRoot, params string[] extra)
    {
        return RunAsync(cacheRoot, ["explain", RuleId, workspace.SolutionPath, .. extra]);
    }

    // Cold, so the load-count delta means "did this run reach MSBuild", with the persisted caches isolated at
    // a private root.
    private static async Task<(CliResult Result, long Loads)> RunAsync(string cacheRoot, params string[] args)
    {
        FakeEnvironment environment = new FakeEnvironment().SetVariable(LoadBearingEnvVars.CacheDirectory, cacheRoot);

        long before = WorkspaceLoader.LoadCount;
        CliResult result = await CliRunner.InvokeColdAsync(environment, args);
        return (result, WorkspaceLoader.LoadCount - before);
    }

    // The output-layout fixture, copied, restored and really built under the default layout. Leased per class
    // and the lease's reset leaves bin/ alone, so the copy, the restore and the build are paid by whichever
    // fact runs first and the other reuses the output.
    private static TempFixtureWorkspace BuiltLayoutCopy()
    {
        var workspace = new TempFixtureWorkspace(
            LayoutAppFixture.FixtureDirectory, LayoutAppFixture.SolutionFileName, restore: false);
        LayoutAppFixture.WritePropsFile(workspace);

        string specAssembly = workspace.PathOf(
            LayoutAppFixture.SpecProject, "bin", "Debug", "net10.0", LayoutAppFixture.SpecAssembly);
        if (File.Exists(specAssembly)) return workspace;

        FixtureRestorer.Restore(workspace.SolutionPath);
        DotnetCli.Run(
            $"build \"{workspace.SolutionPath}\" --disable-build-servers",
            Path.GetDirectoryName(workspace.SolutionPath)!);
        return workspace;
    }
}
