using System.CommandLine;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli;
using Zphil.LoadBearing.Cli.Replay;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Caching;
using Zphil.LoadBearing.Roslyn.Replay;
using Zphil.LoadBearing.Tests.Cli;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Replay;

/// <summary>
///     End-to-end tests for the Phase 12 WP4 CLI wiring of binlog replay: <c>check</c>/<c>status</c>/
///     <c>graph</c> <c>--binlog</c>, the auto-replay of a persisted capture, the stale-capture notice, the
///     ingest refusals, and the <c>--no-cache</c> composition. Each scenario drives the <em>real</em> command
///     tree in-process through <see cref="CliRunner" /> against the shared <see cref="BinlogFixtureWorkspace" />
///     binlog, isolating the persisted caches by pointing <c>LOADBEARING_CACHE_DIR</c> at a throwaway
///     directory per run (the same isolation knob the fragment-cache e2e suite uses, here through the real
///     process env since the real tree reads it via its own <c>SystemEnvironment</c> seam).
/// </summary>
/// <remarks>
///     <para>
///         Two internal observables ride every run, never printed: <see cref="MsBuildGate.LastAcquisition" />
///         (which source-selection branch the gate took) and the delta of
///         <see cref="WorkspaceLoader.LoadCount" /> (whether a design-time build ran) — together the "replayed,
///         no MSBuild" pin. Output stays byte-identical to a cold run in every mode, which is the headline
///         guarantee.
///     </para>
///     <para>
///         In the "Serial" collection: these load an MSBuild workspace on the cold/fallback legs and share the
///         assembly-wide binlog fixture with <see cref="BinlogReplayFidelityTests" /> and
///         <see cref="BinlogCaptureStoreTests" />, which mutate the same tree — so per
///         <see cref="SerialCollection" /> all three live in the one serial world and never run at once. Any
///         mutation of the shared tree is reverted byte- and mtime-safely in a <c>finally</c> (a structural
///         file left newer than the binlog would trip a later test's ingest staleness check).
///     </para>
/// </remarks>
[Collection("Serial")]
public sealed class BinlogCliE2ETests : IDisposable
{
    private readonly string _cacheRootBase =
        Path.Combine(Path.GetTempPath(), "loadbearing-binlog-cli", Guid.NewGuid().ToString("N"));

    private static BinlogFixtureWorkspace Fixture => BinlogFixtureWorkspace.Instance;
    private static string Sln => Fixture.SolutionPath;
    private static string Binlog => Fixture.BinlogPath;
    private static string CleanSpec => CliRunner.CleanSpecDll;

    public void Dispose()
    {
        TryDeleteDirectory(_cacheRootBase);
    }

    // ── (1) byte-parity trio + auto-replay ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Check_ExplicitBinlog_ByteIdenticalToColdThenPersistsAndAutoReplays()
    {
        // (a) plain cold check, fresh cache — the byte-parity baseline (opens a workspace, one design-time build).
        string coldCache = FreshCache();
        GateRun cold = await RunAsync(coldCache, "check", Sln, "--spec", CleanSpec);
        cold.Gate.ShouldBe(GateAcquisition.Cold);
        cold.LoaderDelta.ShouldBe(1);

        // (b) check --binlog, fresh cache — replays the user's binlog: byte-identical, no design-time build,
        //     and the capture (manifest + binlog copy) now sits beside the fragment cache the run also wrote.
        string replayCache = FreshCache();
        GateRun replay = await RunAsync(replayCache, "check", Sln, "--binlog", Binlog, "--spec", CleanSpec);
        replay.Out.ShouldBe(cold.Out);
        replay.Err.ShouldBe(cold.Err);
        replay.Exit.ShouldBe(cold.Exit);
        replay.Gate.ShouldBe(GateAcquisition.ExplicitReplay);
        replay.LoaderDelta.ShouldBe(0);
        File.Exists(CacheLocations.CaptureManifestPath(Sln, replayCache)).ShouldBeTrue();
        File.Exists(CacheLocations.CaptureBinlogPath(Sln, replayCache)).ShouldBeTrue();

        // (c) rerun WITHOUT --binlog on the same cache: (b) wrote fragments, so this is a fragment-cache hit —
        //     no workspace acquired at all, and the gate still decided the capture is usable. Byte-identical.
        GateRun autoHit = await RunAsync(replayCache, "check", Sln, "--spec", CleanSpec);
        autoHit.Out.ShouldBe(cold.Out);
        autoHit.Err.ShouldBe(cold.Err);
        autoHit.Exit.ShouldBe(cold.Exit);
        autoHit.Gate.ShouldBe(GateAcquisition.CaptureReplay);
        autoHit.LoaderDelta.ShouldBe(0);

        // Invalidate ONLY the fragment cache (delete cache.json), leave the capture: now the run must actually
        // replay the capture's binlog copy to get a solution — still no design-time build, still byte-identical.
        File.Delete(CacheLocations.CacheFilePath(Sln, replayCache));
        GateRun autoReplay = await RunAsync(replayCache, "check", Sln, "--spec", CleanSpec);
        autoReplay.Out.ShouldBe(cold.Out);
        autoReplay.Err.ShouldBe(cold.Err);
        autoReplay.Exit.ShouldBe(cold.Exit);
        autoReplay.Gate.ShouldBe(GateAcquisition.CaptureReplay);
        autoReplay.LoaderDelta.ShouldBe(0);
    }

    // ── (2) invalid capture ⇒ one notice + cold fallback ─────────────────────────────────────────────────

    [Fact]
    public async Task Check_StaleCapture_PrintsNoticeOnceAndFallsBackToDesignTimeBuild()
    {
        string cache = FreshCache();
        await RunAsync(cache, "check", Sln, "--binlog", Binlog, "--spec", CleanSpec); // seed the capture (replay)

        string csproj = Fixture.PathOf("MyApp.Domain", "MyApp.Domain.csproj");
        var original = Snapshot(csproj);
        try
        {
            // A harmless XML comment: a structural content change that invalidates the capture (stale) and the
            // fragment cache (a structural miss), without changing the extracted model — so stdout is unmoved.
            string edited = File.ReadAllText(csproj).Replace("</Project>", "  <!-- capture stale probe -->\n</Project>");
            File.WriteAllText(csproj, edited);

            // Baseline: a plain cold run over the edited tree with no capture (fresh cache) — one build, silent.
            GateRun cold = await RunAsync(FreshCache(), "check", Sln, "--spec", CleanSpec);
            cold.LoaderDelta.ShouldBe(1);

            // The seeded cache now holds a stale capture: the gate notices once at acquisition, then builds.
            GateRun invalid = await RunAsync(cache, "check", Sln, "--spec", CleanSpec);
            var notice = $"warning: {BinlogCaptureStore.StaleNotice(Path.GetFullPath(csproj))}";

            invalid.Out.ShouldBe(cold.Out); // stdout byte-identical to the cold run
            invalid.Exit.ShouldBe(cold.Exit);
            invalid.LoaderDelta.ShouldBe(1); // the design-time build ran
            invalid.Gate.ShouldBe(GateAcquisition.NoticeCold);
            // stderr is exactly the one notice line (printed first, at acquisition) followed by the cold stderr.
            invalid.Err.ShouldBe($"{notice}{Environment.NewLine}{cold.Err}");
        }
        finally
        {
            Restore(csproj, original);
        }
    }

    // ── (3) + (4) loud refusals: stale-at-ingest, missing, junk ──────────────────────────────────────────

    [Fact]
    public async Task Check_BinlogStaleAtIngest_ExitsTwoWithPredatesRefusalAndPersistsNothing()
    {
        string cache = FreshCache();
        string csproj = Fixture.PathOf("MyApp.Domain", "MyApp.Domain.csproj");
        DateTime originalMtime = File.GetLastWriteTimeUtc(csproj);
        try
        {
            // Bump a csproj past the binlog with no content change: ingest must refuse the now-stale build.
            File.SetLastWriteTimeUtc(csproj, File.GetLastWriteTimeUtc(Binlog).AddHours(1));

            GateRun run = await RunAsync(cache, "check", Sln, "--binlog", Binlog, "--spec", CleanSpec);

            run.Exit.ShouldBe(2);
            run.Err.Trim().ShouldBe(BinlogCaptureStore.StaleAtIngestMessage(Binlog, Path.GetFullPath(csproj)));
            // The refusal fired before persistence: nothing was written.
            File.Exists(CacheLocations.CaptureManifestPath(Sln, cache)).ShouldBeFalse();
            File.Exists(CacheLocations.CaptureBinlogPath(Sln, cache)).ShouldBeFalse();
        }
        finally
        {
            File.SetLastWriteTimeUtc(csproj, originalMtime);
        }
    }

    [Fact]
    public async Task Check_MissingBinlog_ExitsTwoWithWasNotFound()
    {
        GateRun run = await RunAsync(FreshCache(), "check", Sln, "--binlog", "nope.binlog", "--spec", CleanSpec);

        run.Exit.ShouldBe(2);
        run.Err.Trim().ShouldBe(BinlogReplayMessages.MissingFileMessage("nope.binlog"));
    }

    [Fact]
    public async Task Check_JunkBinlog_ExitsTwoWithCouldNotBeReplayed()
    {
        Directory.CreateDirectory(_cacheRootBase);
        string junk = Path.Combine(_cacheRootBase, "junk.binlog");
        await File.WriteAllTextAsync(junk, "this is not a binlog");

        GateRun run = await RunAsync(FreshCache(), "check", Sln, "--binlog", junk, "--spec", CleanSpec);

        run.Exit.ShouldBe(2);
        run.Err.ShouldContain($"--binlog '{junk}' could not be replayed:");
        run.Err.ShouldContain("Rebuild with -bl and pass the fresh binlog.");
    }

    // ── (5) --no-cache composition ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Check_NoCache_ReplaysExplicitBinlogButPersistsNothingAndIgnoresCapture()
    {
        // With a persisted capture present, `--no-cache` runs cold and never reads it — no notice, capture untouched.
        string seededCache = FreshCache();
        await RunAsync(seededCache, "check", Sln, "--binlog", Binlog, "--spec", CleanSpec); // seed (replay)
        byte[] captureBefore = await File.ReadAllBytesAsync(CacheLocations.CaptureBinlogPath(Sln, seededCache));

        GateRun cold = await RunAsync(seededCache, "check", Sln, "--spec", CleanSpec, "--no-cache");
        cold.LoaderDelta.ShouldBe(1); // cold: the design-time build ran
        cold.Gate.ShouldBe(GateAcquisition.Cold);
        cold.Err.ShouldNotContain("build capture"); // no capture notice on the --no-cache path
        (await File.ReadAllBytesAsync(CacheLocations.CaptureBinlogPath(Sln, seededCache))).ShouldBe(captureBefore);
        File.Exists(CacheLocations.CaptureManifestPath(Sln, seededCache)).ShouldBeTrue();

        // An explicit --binlog under --no-cache still replays for this run (an input, not persisted state) —
        // byte-identical to the cold run above, no design-time build, and it writes nothing anywhere.
        string freshCache = FreshCache();
        GateRun replay = await RunAsync(freshCache, "check", Sln, "--binlog", Binlog, "--spec", CleanSpec, "--no-cache");
        replay.Out.ShouldBe(cold.Out);
        replay.Err.ShouldBe(cold.Err);
        replay.Exit.ShouldBe(cold.Exit);
        replay.Gate.ShouldBe(GateAcquisition.ExplicitReplay);
        replay.LoaderDelta.ShouldBe(0);
        File.Exists(CacheLocations.CaptureManifestPath(Sln, freshCache)).ShouldBeFalse();
        File.Exists(CacheLocations.CaptureBinlogPath(Sln, freshCache)).ShouldBeFalse();
        File.Exists(CacheLocations.CacheFilePath(Sln, freshCache)).ShouldBeFalse();
    }

    // ── (6) graph parity ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Graph_ExplicitBinlog_ByteIdenticalToCold()
    {
        GateRun cold = await RunAsync(FreshCache(), "graph", Sln);
        GateRun replay = await RunAsync(FreshCache(), "graph", Sln, "--binlog", Binlog);

        replay.Out.ShouldBe(cold.Out);
        replay.Err.ShouldBe(cold.Err);
        replay.Exit.ShouldBe(cold.Exit);
        replay.Gate.ShouldBe(GateAcquisition.ExplicitReplay);
        replay.LoaderDelta.ShouldBe(0);
        cold.LoaderDelta.ShouldBe(1);
    }

    // ── (7) runtime replay failure of a usable capture ⇒ notice + cold re-run (no double-render) ──────────

    [Fact]
    public async Task Check_UsableCaptureCopyCorrupt_NoticesAndFallsBackToColdOnce()
    {
        string cache = FreshCache();
        await RunAsync(cache, "check", Sln, "--binlog", Binlog, "--spec", CleanSpec); // seed a usable capture (replay)

        // Corrupt the binlog copy but leave the manifest valid: Validate never re-reads the copy's bytes, so it
        // still reports Usable — the failure only surfaces when the lazy source tries to replay it at runtime.
        await File.WriteAllTextAsync(CacheLocations.CaptureBinlogPath(Sln, cache), "corrupt");
        // Delete the fragment cache so the runner must actually acquire (and thus replay) rather than hit.
        File.Delete(CacheLocations.CacheFilePath(Sln, cache));

        GateRun run = await RunAsync(cache, "check", Sln, "--spec", CleanSpec);

        run.Exit.ShouldBe(0); // the cold re-run succeeded — a torn capture never breaks the run
        run.Gate.ShouldBe(GateAcquisition.CaptureReplayFellBackToCold);
        run.LoaderDelta.ShouldBe(1); // exactly one design-time build (the retry), never two renders
        run.Err.ShouldContain("warning: build capture could not be replayed (");
        run.Err.ShouldContain("); running a design-time build instead. Re-capture: rebuild with -bl and re-run with --binlog.");
    }

    // ── (8) help-text pins for --binlog and the revised --no-cache ───────────────────────────────────────

    [Fact]
    public void Help_BinlogAndRevisedNoCache_MatchDictatedTextOnAllThreeCommands()
    {
        const string binlogHelp =
            "A .binlog from a real build of this solution on this machine. Replays the captured structure "
            + "instead of a design-time build; the capture persists, and later runs replay it automatically "
            + "while structurally valid.";
        const string noCacheHelp =
            "Bypass the persisted caches (extraction fragments and the build capture): always load and extract "
            + "fresh, and write nothing back.";

        RootCommand root = CommandFactory.BuildRootCommand();
        foreach (string commandName in new[] { "check", "status", "graph" })
        {
            Command command = root.Subcommands.First(c => c.Name == commandName);
            command.Options.First(o => o.Name == "--binlog").Description.ShouldBe(binlogHelp);
            command.Options.First(o => o.Name == "--no-cache").Description.ShouldBe(noCacheHelp);
        }
    }

    // ── harness ──────────────────────────────────────────────────────────────────────────────────────────

    private string FreshCache()
    {
        return Path.Combine(_cacheRootBase, Guid.NewGuid().ToString("N"));
    }

    // Drives the real command tree, isolating the persisted caches at LOADBEARING_CACHE_DIR and capturing both
    // internal observables. The serial collection makes the transient env-var write and the static observable
    // reads race-free; the env var is restored (not cleared) so the run-wide default the module initializer
    // set survives for the next serial test.
    private static async Task<GateRun> RunAsync(string cacheDir, params string[] args)
    {
        string? previous = Environment.GetEnvironmentVariable(CodebaseSource.CacheDirectoryVariable);
        Environment.SetEnvironmentVariable(CodebaseSource.CacheDirectoryVariable, cacheDir);
        long loaderBefore = WorkspaceLoader.LoadCount;
        try
        {
            CliResult result = await CliRunner.InvokeAsync(args);
            return new GateRun(
                result.Exit, result.Out, result.Err, MsBuildGate.LastAcquisition, WorkspaceLoader.LoadCount - loaderBefore);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CodebaseSource.CacheDirectoryVariable, previous);
        }
    }

    private static (byte[] bytes, DateTime mtime) Snapshot(string path)
    {
        return (File.ReadAllBytes(path), File.GetLastWriteTimeUtc(path));
    }

    private static void Restore(string path, (byte[] bytes, DateTime mtime) snapshot)
    {
        File.WriteAllBytes(path, snapshot.bytes);
        File.SetLastWriteTimeUtc(path, snapshot.mtime);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // best-effort cleanup of the throwaway cache root
        }
    }

    private sealed record GateRun(int Exit, string Out, string Err, GateAcquisition? Gate, long LoaderDelta);
}