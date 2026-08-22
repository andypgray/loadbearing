namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     Restores the checked-in fixture solutions in the test output directory before any workspace
///     fixture opens them.
/// </summary>
/// <remarks>
///     NuGet restore state (<c>obj/project.assets.json</c>) is gitignored, so a fresh clone — and
///     therefore CI — arrives with unrestored fixture projects. An unrestored fixture loads without its
///     references, silently degrading everything under test. <see cref="EnsureRestored" /> restores
///     exactly once per process, thread-safely.
/// </remarks>
internal static class FixtureRestorer
{
    // Run-once, thread-safe: Lazy (ExecutionAndPublication) guarantees exactly one restore even under
    // concurrent entry; repeat calls are free.
    private static readonly Lazy<bool> RestoreGate = new(RestoreAll);

    private static readonly string[] SolutionExtensions = [".sln", ".slnx"];

    /// <summary>Restores every fixture solution once, the first time it is called; free on repeat.</summary>
    internal static void EnsureRestored()
    {
        _ = RestoreGate.Value;
    }

    private static bool RestoreAll()
    {
        string testSolutionsDir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TestSolutions");
        if (!Directory.Exists(testSolutionsDir)) return true;

        // Every project kind, not just *.csproj. A bed holding a project in another language restores the
        // same way and needs the same assets file — and probing the C# half alone short-circuits the sweep
        // the moment those are restored, leaving the other project unrestored and blamed by the restore
        // detector, which refuses the run.
        bool anyUnrestored = Directory
            .EnumerateFiles(testSolutionsDir, "*.*proj", SearchOption.AllDirectories)
            .Any(projectPath => !File.Exists(
                Path.Combine(Path.GetDirectoryName(projectPath)!, "obj", "project.assets.json")));
        if (!anyUnrestored) return true;

        IEnumerable<string> solutions = Directory
            .EnumerateFiles(testSolutionsDir, "*", SearchOption.AllDirectories)
            .Where(IsSolutionFile);
        foreach (string solutionPath in solutions) Restore(solutionPath);

        return true;
    }

    /// <summary>
    ///     Whether a fixture file is a solution this sweep restores: both full formats, and deliberately not
    ///     <c>.slnf</c>. Restoring a filter is restoring the selected projects of the solution it references,
    ///     and every solution a fixture filter references already stands under this root — while a filter
    ///     fixture whose whole job is to be unreadable would fail a restore, and startup must not depend on
    ///     one. Internal so the accepted set pins without shelling the SDK; matched on the extension rather
    ///     than through a search pattern because a three-character pattern is documented to match longer
    ///     extensions too, which would sweep the filters back in.
    /// </summary>
    internal static bool IsSolutionFile(string path)
    {
        string extension = Path.GetExtension(path);
        return SolutionExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Restores one solution with a clean SDK environment (via <see cref="DotnetCli" />). Shared with
    ///     <see cref="TempFixtureWorkspace" />, which restores its private temp copy of the fixture.
    /// </summary>
    internal static void Restore(string solutionPath)
    {
        // --disable-build-servers plus DotnetCli.Run's node-reuse/MSBuild-server env kills persistence:
        // without it the restore leaves a reused MSBuild worker node (and, on newer SDKs, an MSBuild
        // server) alive, and that lingering child inherits the restore's redirected stdout write-handle,
        // so the drain never reaches EOF. No persistent children means the pipe closes cleanly on exit.
        DotnetCli.Run($"restore \"{solutionPath}\" --disable-build-servers", Path.GetDirectoryName(solutionPath)!);
    }
}
