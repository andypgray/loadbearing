namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     An assembly-shared, lazily-built binlog of a private restored copy of the MyApp fixture — the
///     substrate for the Phase 12 binlog-replay tests. Producing a binlog needs a real
///     <c>dotnet build -bl</c>, which costs seconds, so the copy → restore → build runs at most once per
///     test run behind a static <see cref="Lazy{T}" /> and every test reads the same
///     <see cref="SolutionPath" /> / <see cref="BinlogPath" />.
/// </summary>
/// <remarks>
///     Wraps a <see cref="TempFixtureWorkspace" /> for the copy + restore (reusing its build-artifact
///     skipping, re-restore, and temp-root symlink-canonicalisation), then adds the one binlog build via
///     the same cleaned-environment shelling <see cref="FixtureRestorer" /> uses (<see cref="DotnetCli" />).
///     The binlog bakes in the copy's absolute source paths, so replay reads exactly those files from
///     disk — which is why a freshness test mutates a source file in place under <see cref="PathOf" />
///     rather than relocating the tree. Teardown is best-effort at process exit, matching
///     <see cref="TempFixtureWorkspace" />'s tolerance for a temp tree a build handle may still hold.
/// </remarks>
internal sealed class BinlogFixtureWorkspace
{
    // Lazy(ExecutionAndPublication) so a burst of parallel test classes triggers exactly one build; the
    // Replay tests all live in one class (sequential), but the guarantee is cheap and future-proof.
    private static readonly Lazy<BinlogFixtureWorkspace> Shared = new(BuildOnce);

    private readonly TempFixtureWorkspace _copy;

    private BinlogFixtureWorkspace(TempFixtureWorkspace copy)
    {
        _copy = copy;
    }

    /// <summary>The assembly-shared instance; the copy/restore/build happens on first access.</summary>
    internal static BinlogFixtureWorkspace Instance => Shared.Value;

    /// <summary>Absolute path to the copied solution file (the tree the binlog was built from).</summary>
    internal string SolutionPath => _copy.SolutionPath;

    /// <summary>Absolute path to the binlog produced by building <see cref="SolutionPath" />.</summary>
    internal string BinlogPath => _copy.PathOf("MyApp.binlog");

    /// <summary>Absolute path to a file or directory inside the copy, from solution-relative segments.</summary>
    internal string PathOf(params string[] relativeSegments)
    {
        return _copy.PathOf(relativeSegments);
    }

    private static BinlogFixtureWorkspace BuildOnce()
    {
        var copy = new TempFixtureWorkspace();
        var fixture = new BinlogFixtureWorkspace(copy);

        // One real build with the binary logger. --disable-build-servers (plus DotnetCli's node/server env)
        // keeps the drained child from leaving a persistent worker that would wedge the output pipe.
        DotnetCli.Run(
            $"build \"{copy.SolutionPath}\" -bl:LogFile=\"{fixture.BinlogPath}\" --disable-build-servers",
            Path.GetDirectoryName(copy.SolutionPath)!);

        // No per-test disposal hook owns an assembly-shared static; best-effort teardown at process exit.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => copy.Dispose();
        return fixture;
    }
}