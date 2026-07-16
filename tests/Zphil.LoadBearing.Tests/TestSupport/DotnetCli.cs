using System.Diagnostics;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     Runs a <c>dotnet</c> CLI command in a clean SDK environment, draining and bounding it through
///     <see cref="ProcessRunner" /> and throwing with captured output on a non-zero exit. Shared by
///     <see cref="FixtureRestorer" /> (fixture restore) and <see cref="BinlogFixtureWorkspace" /> (the
///     one-shot <c>build -bl</c> that produces the replay binlog) so both get identical poisoned-env
///     stripping and node/server suppression rather than duplicating it. The env hygiene is exposed via
///     <see cref="ApplyCleanSdkEnvironment" /> so an out-of-process CLI test can shape the same
///     deployment-normal environment from the one poison-var list.
/// </summary>
internal static class DotnetCli
{
    /// <summary>
    ///     Runs <c>dotnet <paramref name="arguments" /></c> in <paramref name="workingDirectory" />,
    ///     throwing <see cref="InvalidOperationException" /> (carrying stdout + stderr) on a non-zero exit.
    /// </summary>
    internal static void Run(string arguments, string workingDirectory)
    {
        ProcessStartInfo startInfo = new("dotnet", arguments)
        {
            WorkingDirectory = workingDirectory
        };
        ApplyCleanSdkEnvironment(startInfo);

        ProcessRunner.ProcessResult result = ProcessRunner.Run(startInfo);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"'dotnet {arguments}' failed with exit code {result.ExitCode}."
                + $"{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}{result.StandardError}");
    }

    /// <summary>
    ///     Gives <paramref name="startInfo" /> a clean SDK environment: node-reuse + MSBuild-server off, and
    ///     the test host's Visual Studio / MSBuild registration env vars stripped. Shared so every child that
    ///     shells the SDK — restore, the binlog build, and the out-of-process CLI replay smoke test — sees the
    ///     same deployment-normal environment from a single poison-var list.
    /// </summary>
    /// <remarks>
    ///     Node-reuse + MSBuild-server off: otherwise a reused worker node (or, on newer SDKs, an MSBuild
    ///     server) lingers after the command exits, inherits the child's redirected stdout write-handle, and
    ///     the pipe never reaches EOF — the <see cref="ProcessRunner" /> drain would then unblock only at its
    ///     timeout. (Build callers additionally pass <c>--disable-build-servers</c> for the same reason.) The
    ///     stripped vars matter because the test process has Visual Studio's MSBuild registered (MSBuildLocator
    ///     + <c>MsBuildBootstrap</c> set MSBUILD_EXE_PATH / VSINSTALLDIR / VSCMD_VER process-wide for the
    ///     Roslyn BuildHost); a child inheriting those resolves the wrong MSBuild and fails immediately.
    /// </remarks>
    internal static void ApplyCleanSdkEnvironment(ProcessStartInfo startInfo)
    {
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        startInfo.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";

        foreach (string poisonedVariable in new[]
                 {
                     "MSBUILD_EXE_PATH", "MSBuildExtensionsPath", "MSBuildSDKsPath",
                     "VSINSTALLDIR", "VSCMD_VER", "VisualStudioVersion"
                 })
            startInfo.Environment.Remove(poisonedVariable);
    }
}