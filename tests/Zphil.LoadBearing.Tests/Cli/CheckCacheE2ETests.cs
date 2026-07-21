using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Caching;
using Zphil.LoadBearing.Tests.Mcp.TestDoubles;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     End-to-end tests for the persisted extraction cache — the CLI wiring that makes a
///     clean-tree <c>check</c> skip MSBuild entirely. Each test drives the real runners over an injected
///     counting <see cref="ISolutionSource" /> and a <see cref="FakeEnvironment" /> pointing the cache root
///     at a private temp directory, so a run's <see cref="CodebaseSource" /> outcome, the set of projects it
///     re-extracted, and whether it opened a workspace at all are all observable — while stdout/stderr/exit
///     stay byte-identical to a cold run in every mode. Serialized with the rest of the workspace-loading
///     suites: the miss/partial paths open a real <c>MSBuildWorkspace</c>.
/// </summary>
[Collection("Serial")]
public sealed class CheckCacheE2ETests
{
    private const string Domain = "MyApp.Domain";
    private const string Web = "MyApp.Web";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ── fragment cache: hit / disabled / partial / shared store ───────────────────────────────────────────

    [Fact]
    public async Task Check_ColdThenIdenticalRerun_SecondRunHitsWithByteIdenticalOutputAndNoWorkspace()
    {
        using var cache = new TempCacheDir();

        CacheRun cold = await RunCheckAsync(CliRunner.MyAppSolution, CliRunner.ViolatedSpecDll, cache.Root);
        CacheRun warm = await RunCheckAsync(CliRunner.MyAppSolution, CliRunner.ViolatedSpecDll, cache.Root);

        // The first run had to open the workspace and extract; the second reused the cache with none of that.
        cold.Outcome.ShouldBe(CodebaseSourceOutcome.Miss);
        cold.AcquireCount.ShouldBe(1);
        warm.Outcome.ShouldBe(CodebaseSourceOutcome.Hit);
        warm.AcquireCount.ShouldBe(0);

        // Byte-identical: stdout, stderr, and exit code all match — the cache never leaks into observable output.
        warm.Out.ShouldBe(cold.Out);
        warm.Err.ShouldBe(cold.Err);
        warm.Exit.ShouldBe(cold.Exit);
    }

    [Fact]
    public async Task Check_NoCacheBothRuns_DisabledAndWritesNoCacheFile()
    {
        using var cache = new TempCacheDir();

        CacheRun first = await RunCheckAsync(CliRunner.MyAppSolution, CliRunner.CleanSpecDll, cache.Root, true);
        CacheRun second = await RunCheckAsync(CliRunner.MyAppSolution, CliRunner.CleanSpecDll, cache.Root, true);

        first.Outcome.ShouldBe(CodebaseSourceOutcome.Disabled);
        second.Outcome.ShouldBe(CodebaseSourceOutcome.Disabled);
        first.AcquireCount.ShouldBe(1); // both runs still load a workspace — that is what "disabled" means
        second.AcquireCount.ShouldBe(1);

        // --no-cache never writes: no cache.json exists anywhere under the root.
        cache.HasCacheFile().ShouldBeFalse();

        second.Out.ShouldBe(first.Out);
        second.Err.ShouldBe(first.Err);
        second.Exit.ShouldBe(first.Exit);
    }

    [Fact]
    public async Task Graph_SourceEditedBetweenRuns_PartialReExtractsDirtyPlusDependentsAndMatchesFreshCold()
    {
        using var workspace = new TempFixtureWorkspace();
        using var cache = new TempCacheDir();

        // Populate the cache from the clean tree.
        CacheRun cold = await RunGraphAsync(workspace.SolutionPath, cache.Root);
        cold.Outcome.ShouldBe(CodebaseSourceOutcome.Miss);

        // Add a new type to the Web project (an SDK-glob add the cone scan catches). Web is content-dirty;
        // Domain references Web, so its Merkle key changes too — Billing (which Web references, but which
        // references nothing) stays clean and is reused.
        await AddSourceFileAsync(workspace.PathOf(Web, "CacheProbe.cs"), "namespace MyApp.Web;\n\npublic class CacheProbeType;\n");

        CacheRun partial = await RunGraphAsync(workspace.SolutionPath, cache.Root);
        CacheRun freshCold = await RunGraphAsync(workspace.SolutionPath, cache.Root, true);

        // Re-extraction covers exactly the content-dirty project and its Merkle dependents — nothing else.
        partial.Outcome.ShouldBe(CodebaseSourceOutcome.Partial);
        partial.ReExtracted.ShouldBe(new[] { Web, Domain }, true);

        // The partial model equals a full cold extraction on the edited tree, and the edit really was seen.
        partial.Out.ShouldBe(freshCold.Out);
        partial.Err.ShouldBe(freshCold.Err);
        partial.Exit.ShouldBe(freshCold.Exit);
        partial.Out.ShouldNotBe(cold.Out);
    }

    [Fact]
    public async Task GraphThenCheck_ShareOneStore_CheckHitsOnGraphsFragments()
    {
        using var cache = new TempCacheDir();

        // graph extracts every project (no spec, no exclusion) and writes them all.
        CacheRun graph = await RunGraphAsync(CliRunner.MyAppSolution, cache.Root);
        graph.Outcome.ShouldBe(CodebaseSourceOutcome.Miss);

        // check reuses those very fragments: the external DLL spec resolves with no workspace, so it is a hit.
        CacheRun check = await RunCheckAsync(CliRunner.MyAppSolution, CliRunner.CleanSpecDll, cache.Root);
        check.Outcome.ShouldBe(CodebaseSourceOutcome.Hit);
        check.AcquireCount.ShouldBe(0);
        check.Exit.ShouldBe(0);
    }

    // ── spec replay on a hit: convention / csproj / explicit-DLL / sibling-config fallback ─────────────────
    //
    // These pin the workspace-free spec resolution a hit performs. The member-spec (convention/csproj)
    // *exclude* on a hit shares the one Retain filter the cold path already exercises under SelfSpecTests
    // (which extracts all projects and merges minus the arch-spec project), so it is covered there. A
    // full-repo hit was once impossible to assert end-to-end because this repo's test project embeds
    // non-compiled fixture sources under its own directory, and the store's cone scan read them as perpetual
    // adds; the fix (cone-adds recorded identically at capture and validation) cures that, but these
    // targeted cases stay the right level to pin the spec-replay path without a whole-repo build.

    [Fact]
    public void ResolveSpecOnHit_ExplicitDllPresent_ResolvesWithoutAnyRecord()
    {
        SpecResolution? resolution = CodebaseSource.ResolveSpecOnHit(CliRunner.CleanSpecDll, []);

        resolution.ShouldNotBeNull();
        resolution.DllPath.ShouldBe(Path.GetFullPath(CliRunner.CleanSpecDll));
        resolution.ExcludeProjectName.ShouldBeNull(); // an external DLL excludes no solution project
    }

    [Fact]
    public void ResolveSpecOnHit_ExplicitDllMissing_ThrowsTheSameLoudErrorAsCold()
    {
        var error =
            Should.Throw<UserErrorException>(() => CodebaseSource.ResolveSpecOnHit("does-not-exist.dll", []));

        error.Message.ShouldContain("was not found");
    }

    [Fact]
    public void ResolveSpecOnHit_ConventionWithNoMatchingRecord_ReturnsNullForColdFallback()
    {
        CodebaseSource.ResolveSpecOnHit(null, []).ShouldBeNull();
    }

    [Fact]
    public void ResolveSpecOnHit_ConventionRecordEvaluatedOutputPresent_ResolvesRecordedOutput()
    {
        // The recorded (Debug-evaluated) output exists, so RequireBuiltOutput returns it directly.
        var records = new[] { new SpecResolutionRecord("", "MyApp.Arch", CliRunner.CleanSpecDll) };

        SpecResolution? resolution = CodebaseSource.ResolveSpecOnHit(null, records);

        resolution.ShouldNotBeNull();
        resolution.DllPath.ShouldBe(CliRunner.CleanSpecDll);
        resolution.ExcludeProjectName.ShouldBe("MyApp.Arch");
    }

    [Fact]
    public void ResolveSpecOnHit_ConventionRecordEvaluatedConfigMissingButSiblingBuilt_ResolvesSiblingConfiguration()
    {
        // Mirrors the SpecResolver sibling-configuration test (commit ce899f2): a hit re-runs the built-output
        // check over the recorded Debug path, and when only Release was built it must resolve the Release DLL —
        // identical fallback and identical error text to a cold run.
        string root = Path.Combine(Path.GetTempPath(), "loadbearing-cache-specreplay", Guid.NewGuid().ToString("N"));
        string evaluatedDebug = Path.Combine(root, "bin", "Debug", "net10.0", "MyApp.Arch.dll");
        string builtRelease = Path.Combine(root, "bin", "Release", "net10.0", "MyApp.Arch.dll");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(builtRelease)!);
            File.WriteAllText(builtRelease, "");
            var records = new[] { new SpecResolutionRecord("", "MyApp.Arch", evaluatedDebug) };

            SpecResolution? resolution = CodebaseSource.ResolveSpecOnHit(null, records);

            resolution.ShouldNotBeNull();
            resolution.DllPath.ShouldBe(builtRelease);
            resolution.ExcludeProjectName.ShouldBe("MyApp.Arch");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ResolveSpecOnHit_CsprojRecordMatchedByNormalizedPath_ResolvesRecordedOutput()
    {
        // A csproj --spec is looked up by its normalized (full) path, not the raw argument string.
        const string csprojArgument = "spec/MyApp.Arch.csproj";
        string normalized = Path.GetFullPath(csprojArgument);
        var records = new[] { new SpecResolutionRecord(normalized, "MyApp.Arch", CliRunner.CleanSpecDll) };

        SpecResolution? resolution = CodebaseSource.ResolveSpecOnHit(csprojArgument, records);

        resolution.ShouldNotBeNull();
        resolution.DllPath.ShouldBe(CliRunner.CleanSpecDll);
        resolution.ExcludeProjectName.ShouldBe("MyApp.Arch");
    }

    // ── harness ───────────────────────────────────────────────────────────────────────────────────────────

    private static async Task<CacheRun> RunCheckAsync(string solution, string spec, string cacheRoot, bool noCache = false)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var counting = new CountingSolutionSource();
        var runner = new CheckRunner(output, error, counting, EnvironmentFor(cacheRoot));

        int exit = await runner.RunAsync(
            new CheckRequest(solution, spec, true, null, WorkingDirectoryOf(solution), noCache, null, false), Ct);

        return new CacheRun(
            exit, output.ToString(), error.ToString(), runner.LastOutcome, runner.LastReExtractedProjects, counting.AcquireCount);
    }

    private static async Task<CacheRun> RunGraphAsync(string solution, string cacheRoot, bool noCache = false)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var counting = new CountingSolutionSource();
        var runner = new GraphRunner(output, error, counting, EnvironmentFor(cacheRoot));

        int exit = await runner.RunAsync(
            new GraphRequest(solution, true, WorkingDirectoryOf(solution), noCache, null), Ct);

        return new CacheRun(
            exit, output.ToString(), error.ToString(), runner.LastOutcome, runner.LastReExtractedProjects, counting.AcquireCount);
    }

    private static FakeEnvironment EnvironmentFor(string cacheRoot)
    {
        return new FakeEnvironment().SetVariable(CodebaseSource.CacheDirectoryVariable, cacheRoot);
    }

    private static string WorkingDirectoryOf(string solution)
    {
        return Path.GetDirectoryName(Path.GetFullPath(solution))!;
    }

    // Writes a brand-new source file the SDK glob will compile in — an add only the cone scan can see.
    private static async Task AddSourceFileAsync(string filePath, string content)
    {
        await File.WriteAllTextAsync(filePath, content, Ct);
    }

    private sealed record CacheRun(
        int Exit,
        string Out,
        string Err,
        CodebaseSourceOutcome? Outcome,
        IReadOnlySet<string> ReExtracted,
        int AcquireCount);

    // Counts workspace acquisitions so a hit (zero acquisitions) is observable without any timing.
    private sealed class CountingSolutionSource : ISolutionSource
    {
        private readonly ColdSolutionSource inner = new();

        public int AcquireCount { get; private set; }

        public Task<SolutionHandle> AcquireAsync(string? solution, string workingDirectory, CancellationToken ct)
        {
            AcquireCount++;
            return inner.AcquireAsync(solution, workingDirectory, ct);
        }
    }

    private sealed class TempCacheDir : IDisposable
    {
        public string Root { get; } =
            Path.Combine(Path.GetTempPath(), "loadbearing-cache-e2e", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, true);
            }
            catch
            {
                // best-effort temp cleanup
            }
        }

        public bool HasCacheFile()
        {
            return Directory.Exists(Root)
                   && Directory.EnumerateFiles(Root, "cache.json", SearchOption.AllDirectories).Any();
        }
    }
}