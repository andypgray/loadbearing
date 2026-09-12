using System.Text.Json;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Pipeline;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Cli.SpecLoading;
using Zphil.LoadBearing.Cli.Verbs;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Caching;
using Zphil.LoadBearing.Roslyn.Hosting;
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

    // ── fragment cache: hit / disabled / partial / shared store ───────────────────────────────────────────

    [Fact]
    public async Task Check_ColdThenIdenticalRerun_SecondRunHitsWithByteIdenticalOutputAndNoWorkspace()
    {
        using var cache = new TempCacheRoot("check-cache");

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
        using var cache = new TempCacheRoot("check-cache");

        CacheRun first = await RunCheckAsync(CliRunner.MyAppSolution, CliRunner.CleanSpecDll, cache.Root, true);
        CacheRun second = await RunCheckAsync(CliRunner.MyAppSolution, CliRunner.CleanSpecDll, cache.Root, true);

        first.Outcome.ShouldBe(CodebaseSourceOutcome.Disabled);
        second.Outcome.ShouldBe(CodebaseSourceOutcome.Disabled);
        first.AcquireCount.ShouldBe(1); // both runs still load a workspace — that is what "disabled" means
        second.AcquireCount.ShouldBe(1);

        // --no-cache never writes: no cache.json exists anywhere under the root.
        cache.HasCacheFile()
            .ShouldBeFalse();

        second.Out.ShouldBe(first.Out);
        second.Err.ShouldBe(first.Err);
        second.Exit.ShouldBe(first.Exit);
    }

    [Fact]
    public async Task Graph_SourceEditedBetweenRuns_PartialReExtractsDirtyPlusDependentsAndMatchesFreshCold()
    {
        using var workspace = new TempFixtureWorkspace();
        using var cache = new TempCacheRoot("check-cache");

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
        partial.ReExtracted.ShouldBe([Web, Domain], true);

        // The partial model equals a full cold extraction on the edited tree, and the edit really was seen.
        partial.Out.ShouldBe(freshCold.Out);
        partial.Err.ShouldBe(freshCold.Err);
        partial.Exit.ShouldBe(freshCold.Exit);
        partial.Out.ShouldNotBe(cold.Out);
    }

    [Fact]
    public async Task Check_CommentOnlyEditBetweenRuns_HitsWithSitesMovedAndMatchesAFreshColdRun()
    {
        using var workspace = new TempFixtureWorkspace();
        using var cache = new TempCacheRoot("check-cache");
        using TempDirectory logs = TestTempRoot.Fresh("check-cache-sarif");
        string coldSarif = logs.PathOf("cold.sarif");
        string warmSarif = logs.PathOf("warm.sarif");
        string freshSarif = logs.PathOf("fresh.sarif");

        // Populate the cache from the clean tree.
        CacheRun cold = await RunCheckAsync(
            workspace.SolutionPath, CliRunner.ViolatedSpecDll, cache.Root, sarif: coldSarif);
        cold.Outcome.ShouldBe(CodebaseSourceOutcome.Miss);

        // Two comment lines above each controller — the red one and the grandfathered one. Every fact the
        // stored fragments hold is still true; only the lines they hold them at have moved, by exactly two.
        FixtureEdits.EditOnDisk(workspace.PathOf(Web, "HomeController.cs"), Prepended);
        FixtureEdits.EditOnDisk(workspace.PathOf(Web, "InvoiceController.cs"), Prepended);

        CacheRun warm = await RunCheckAsync(
            workspace.SolutionPath, CliRunner.ViolatedSpecDll, cache.Root, sarif: warmSarif);
        CacheRun fresh = await RunCheckAsync(
            workspace.SolutionPath, CliRunner.ViolatedSpecDll, cache.Root, true, freshSarif);

        // Nothing was re-extracted and no workspace was opened: a comment edit is a hit, not a partial.
        warm.Outcome.ShouldBe(CodebaseSourceOutcome.Hit);
        warm.AcquireCount.ShouldBe(0);
        warm.ReExtracted.ShouldBeEmpty();

        // And the hit answers what a full cold extraction of the edited tree answers, on every channel.
        warm.Out.ShouldBe(fresh.Out);
        warm.Err.ShouldBe(fresh.Err);
        warm.Exit.ShouldBe(fresh.Exit);
        File.ReadAllBytes(warmSarif)
            .ShouldBe(File.ReadAllBytes(freshSarif));

        // The edit really was seen — a hit that replayed the stored lines would match the cold run instead.
        warm.Out.ShouldNotBe(cold.Out);

        // Site by site: the grandfathered notes and the red error each moved down by the two lines inserted
        // above them, and no site was gained or lost on the way.
        string coldLog = File.ReadAllText(coldSarif);
        string warmLog = File.ReadAllText(warmSarif);
        InlineSqlSiteLines(warmLog, "note")
            .ShouldBe(MovedDownByTwo(InlineSqlSiteLines(coldLog, "note")));
        InlineSqlSiteLines(warmLog, "error")
            .ShouldBe(MovedDownByTwo(InlineSqlSiteLines(coldLog, "error")));
    }

    [Fact]
    public async Task GraphThenCheck_ShareOneStore_CheckHitsOnGraphsFragments()
    {
        using var cache = new TempCacheRoot("check-cache");

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
        resolution.SpecProjectName.ShouldBeNull(); // an external DLL excludes no solution project
        resolution.ExcludeProjectNames.ShouldBeEmpty();
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
        CodebaseSource.ResolveSpecOnHit(null, [])
            .ShouldBeNull();
    }

    [Fact]
    public void ResolveSpecOnHit_ConventionRecordEvaluatedOutputPresent_ResolvesRecordedOutput()
    {
        // The recorded (Debug-evaluated) output exists, so RequireBuiltOutput returns it directly. The whole
        // recorded exclusion set replays: a hit has no workspace to re-walk the spec's reference closure with.
        var records = new[]
        {
            new SpecResolutionRecord(
                "", "MyApp.Arch", ["MyApp.Arch", "MyApp.Arch.Pack"], [CliRunner.CleanSpecDll], null)
        };

        SpecResolution? resolution = CodebaseSource.ResolveSpecOnHit(null, records);

        resolution.ShouldNotBeNull();
        resolution.DllPath.ShouldBe(CliRunner.CleanSpecDll);
        resolution.SpecProjectName.ShouldBe("MyApp.Arch");
        resolution.ExcludeProjectNames.ShouldBe(["MyApp.Arch", "MyApp.Arch.Pack"]);
    }

    [Fact]
    public void ResolveSpecOnHit_ConventionRecordEvaluatedConfigMissingButSiblingBuilt_ResolvesSiblingConfiguration()
    {
        // An exact mirror of the cold case in SpecResolverTests, down to the intermediate assembly in the tree
        // and in the record: a hit re-runs the built-output check over the same two inputs the cold path had —
        // the recorded Debug path and the recorded obj-side assembly — so the search cannot diverge between
        // them. Only Release was built, so the Release DLL is the answer on both paths.
        using TempDirectory temp = TestTempRoot.Fresh("cache-spec-replay");
        string evaluatedDebug = temp.PathOf("bin", "Debug", "net10.0", "MyApp.Arch.dll");
        string builtRelease = WriteAssembly(temp, "bin", "Release", "net10.0", "MyApp.Arch.dll");
        string intermediate = WriteAssembly(temp, "obj", "Release", "net10.0", "MyApp.Arch.dll");
        var records = new[]
        {
            new SpecResolutionRecord("", "MyApp.Arch", ["MyApp.Arch"], [evaluatedDebug], intermediate)
        };

        SpecResolution? resolution = CodebaseSource.ResolveSpecOnHit(null, records);

        resolution.ShouldNotBeNull();
        resolution.DllPath.ShouldBe(builtRelease);
        resolution.ExcludeProjectNames.ShouldBe(["MyApp.Arch"]);
    }

    [Fact]
    public void ResolveSpecOnHit_RecordCarriesTheIntermediatePath_RefusesAnIntermediateResultExactlyAsColdDoes()
    {
        // Why the recorded intermediate path is worth a cache schema version. The tree is the one where the
        // anchor walk alone cannot keep the obj-side assembly out of scope — BaseIntermediateOutputPath
        // redirected under the output root — so the only thing that refuses it is the recorded path. Without
        // that field a hit would load an intermediate assembly where a cold run refuses, which is a hit
        // answering differently from the cold run it replays.
        using TempDirectory temp = TestTempRoot.Fresh("cache-spec-replay");
        string evaluatedDebug = temp.PathOf("bin", "Debug", "net10.0", "MyApp.Arch.dll");
        string intermediate = WriteAssembly(temp, "bin", "obj", "Debug", "net10.0", "MyApp.Arch.dll");
        var recorded = new[]
        {
            new SpecResolutionRecord("", "MyApp.Arch", ["MyApp.Arch"], [evaluatedDebug], intermediate)
        };

        var error = Should.Throw<UserErrorException>(() => CodebaseSource.ResolveSpecOnHit(null, recorded));
        error.Message.ShouldContain("Build the solution first (dotnet build).");

        // The negative control, on the same tree: a record from before the field existed deserializes with a
        // null intermediate path and resolves the obj-side assembly — the behaviour the schema bump exists to
        // keep out of a hit.
        var unrecorded = new[]
        {
            new SpecResolutionRecord("", "MyApp.Arch", ["MyApp.Arch"], [evaluatedDebug], null)
        };

        SpecResolution? unrefused = CodebaseSource.ResolveSpecOnHit(null, unrecorded);

        unrefused.ShouldNotBeNull();
        unrefused.DllPath.ShouldBe(intermediate);
    }

    [Fact]
    public void ResolveSpecOnHit_CsprojRecordMatchedByNormalizedPath_ResolvesRecordedOutput()
    {
        // A csproj --spec is looked up by its normalized (full) path, not the raw argument string.
        const string csprojArgument = "spec/MyApp.Arch.csproj";
        string normalized = Path.GetFullPath(csprojArgument);
        var records = new[]
        {
            new SpecResolutionRecord(normalized, "MyApp.Arch", ["MyApp.Arch"], [CliRunner.CleanSpecDll], null)
        };

        SpecResolution? resolution = CodebaseSource.ResolveSpecOnHit(csprojArgument, records);

        resolution.ShouldNotBeNull();
        resolution.DllPath.ShouldBe(CliRunner.CleanSpecDll);
        resolution.ExcludeProjectNames.ShouldBe(["MyApp.Arch"]);
    }

    // ── harness ───────────────────────────────────────────────────────────────────────────────────────────

    private static async Task<CacheRun> RunCheckAsync(
        string solution, string spec, string cacheRoot, bool noCache = false, string? sarif = null)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var counting = new CountingSolutionSource();
        var runner = new CheckRunner(output, error, counting, EnvironmentFor(cacheRoot));

        CheckRequest request = CheckRequests.For(solution, spec, SolutionPaths.SolutionDirectoryOf(solution))
            with
            {
                Json = true, NoCache = noCache, Sarif = sarif
            };

        int exit = await runner.RunAsync(request, Ct);

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
            new GraphRequest(solution, true, SolutionPaths.SolutionDirectoryOf(solution), noCache, null, false, DocumentGrain.Full, null), Ct);

        return new CacheRun(
            exit, output.ToString(), error.ToString(), runner.LastOutcome, runner.LastReExtractedProjects, counting.AcquireCount);
    }

    private static FakeEnvironment EnvironmentFor(string cacheRoot)
    {
        return new FakeEnvironment().SetVariable(LoadBearingEnvVars.CacheDirectory, cacheRoot);
    }

    // Writes a brand-new source file the SDK glob will compile in — an add only the cone scan can see.
    private static async Task AddSourceFileAsync(string filePath, string content)
    {
        await File.WriteAllTextAsync(filePath, content, Ct);
    }

    // Two comment lines above everything else: the one edit that changes a file's bytes and none of its
    // facts, and moves every site in it down by exactly two.
    private static string Prepended(string source)
    {
        return "// a note about this controller\n// and a second line of it\n" + source;
    }

    private static IReadOnlyList<int> MovedDownByTwo(IReadOnlyList<int> lines)
    {
        return lines.Select(line => line + 2)
            .ToList();
    }

    // The inline-SQL rule's result lines at one SARIF level, in render order.
    private static IReadOnlyList<int> InlineSqlSiteLines(string sarif, string level)
    {
        IReadOnlyList<int> lines = sarif.SarifResults()
            .Where(result => result.GetProperty("ruleId")
                .GetString() == "data-access/no-inline-sql")
            .Where(result => result.GetProperty("level")
                .GetString() == level)
            .Select(StartLine)
            .ToList();
        lines.ShouldNotBeEmpty();
        return lines;
    }

    // The 1-based source line of a single-location result — the site the reader is sent to.
    private static int StartLine(JsonElement result)
    {
        return result.GetProperty("locations")[0]
            .GetProperty("physicalLocation")
            .GetProperty("region")
            .GetProperty("startLine")
            .GetInt32();
    }

    // A zero-byte assembly for the spec-replay trees: the built-output check resolves a path, it never reads
    // the bytes behind it.
    private static string WriteAssembly(TempDirectory temp, params string[] segments)
    {
        return temp.WriteFile(segments, "");
    }

    private sealed record CacheRun(
        int Exit,
        string Out,
        string Err,
        CodebaseSourceOutcome? Outcome,
        IReadOnlySet<string> ReExtracted,
        int AcquireCount);
}
