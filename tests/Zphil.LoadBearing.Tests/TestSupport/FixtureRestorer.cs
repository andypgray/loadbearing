using System.Diagnostics;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     Restores the checked-in fixture solutions in the test output directory before any workspace
///     fixture opens them.
/// </summary>
/// <remarks>
///     <para>
///         NuGet restore state (<c>obj/project.assets.json</c>) is gitignored, so a fresh clone — and
///         therefore CI — arrives with unrestored fixture projects. An unrestored fixture loads
///         without its references, silently degrading everything under test. <see cref="EnsureRestored" />
///         restores exactly once per process, thread-safely.
///     </para>
///     <para>
///         <b>
///             Driven from <see cref="FixtureRestoreStartup" /> (an xUnit v3 pipeline-startup hook),
///             not a <c>[ModuleInitializer]</c>:
///         </b>
///         module initializers also run during the runner's
///         assembly-info probe, whose 60-second no-response deadline a cold restore (minutes) blows
///         past, timing out discovery so zero tests run. Pipeline startup runs only in the
///         discover/run pass, which has no such deadline.
///     </para>
/// </remarks>
internal static class FixtureRestorer
{
    // Run-once, thread-safe: Lazy (ExecutionAndPublication) guarantees exactly one restore even when
    // called from more than one place; repeat calls are free.
    private static readonly Lazy<bool> RestoreGate = new(RestoreAll);

    /// <summary>Restores every fixture solution once, the first time it is called; free on repeat.</summary>
    internal static void EnsureRestored()
    {
        _ = RestoreGate.Value;
    }

    private static bool RestoreAll()
    {
        string testSolutionsDir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TestSolutions");
        if (!Directory.Exists(testSolutionsDir)) return true;

        bool anyUnrestored = Directory
            .EnumerateFiles(testSolutionsDir, "*.csproj", SearchOption.AllDirectories)
            .Any(projectPath => !File.Exists(
                Path.Combine(Path.GetDirectoryName(projectPath)!, "obj", "project.assets.json")));
        if (!anyUnrestored) return true;

        foreach (string solutionPath in Directory
                     .EnumerateFiles(testSolutionsDir, "*.sln", SearchOption.AllDirectories))
            Restore(solutionPath);

        return true;
    }

    /// <summary>
    ///     Restores one solution with a clean SDK environment (the poisoned-env stripping below). Shared
    ///     with <see cref="TempFixtureWorkspace" />, which restores its private temp copy of the fixture.
    /// </summary>
    internal static void Restore(string solutionPath)
    {
        // --disable-build-servers plus the node-reuse/MSBuild-server env kills persistence: without it the
        // restore leaves a reused MSBuild worker node (and, on newer SDKs, an MSBuild server) alive, and
        // that lingering child inherits the restore's redirected stdout write-handle — so the pipe never
        // reaches EOF after the restore exits and the parent's read wedges (bounded only by ProcessRunner's
        // timeout). No persistent children means the pipe closes cleanly on exit.
        ProcessStartInfo startInfo = new("dotnet", $"restore \"{solutionPath}\" --disable-build-servers")
        {
            WorkingDirectory = Path.GetDirectoryName(solutionPath)!
        };
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        startInfo.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";

        // The test process has Visual Studio's MSBuild registered (MSBuildLocator + MsBuildBootstrap
        // set MSBUILD_EXE_PATH / VSINSTALLDIR / VSCMD_VER process-wide for the Roslyn BuildHost). A
        // child dotnet-CLI restore inheriting those resolves the wrong MSBuild and fails immediately,
        // so give it a clean SDK environment.
        foreach (string poisonedVariable in new[]
                 {
                     "MSBUILD_EXE_PATH", "MSBuildExtensionsPath", "MSBuildSDKsPath",
                     "VSINSTALLDIR", "VSCMD_VER", "VisualStudioVersion"
                 })
            startInfo.Environment.Remove(poisonedVariable);

        // Drain through ProcessRunner so any residual handle-inheritance wedge is bounded, not infinite.
        ProcessRunner.ProcessResult result = ProcessRunner.Run(startInfo);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"'dotnet restore {solutionPath}' failed with exit code {result.ExitCode}."
                + $"{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}{result.StandardError}");
    }
}