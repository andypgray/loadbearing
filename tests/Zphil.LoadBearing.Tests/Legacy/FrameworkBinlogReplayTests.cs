using System.Diagnostics;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli;
using Zphil.LoadBearing.Cli.Replay;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Tests.Cli;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Legacy;

/// <summary>
///     The last leg of the legacy bridge: a binlog produced by .NET Framework <c>MSBuild.exe</c> — the
///     build a classic build server actually runs — replayed by the .NET 10 CLI. The oracle is byte
///     parity: <c>check --binlog</c> over that binlog and a plain <c>check</c> that opens the workspace
///     itself must produce the same output, so nothing about the replay path is a lesser reading of the
///     codebase. The replay leg additionally opens no workspace at all, which is the claim in its
///     strongest form: the machine that builds needs no .NET 10, only the machine that analyses.
/// </summary>
/// <remarks>
///     Gated twice over: Windows, and a Visual Studio or Build Tools install carrying
///     <c>MSBuild\Current\Bin\MSBuild.exe</c>. Rather than probing for one, the test reads the install
///     root <c>MsBuildBootstrap</c> already chose and published — no second selection policy, and the
///     MSBuild it drives is the one the product picked. Going through <c>VsWhereLocator</c> directly would
///     reach inside the <c>roslyn/msbuild-bootstrap</c> quarantine, which this repository's own spec
///     forbids outside <c>MsBuildBootstrap</c>.
///     In the "Serial" collection: the parity leg opens a real MSBuild workspace.
/// </remarks>
[Collection("Serial")]
public sealed class FrameworkBinlogReplayTests
{
    [Fact]
    public async Task Check_FrameworkProducedBinlog_ReplaysByteIdenticallyWithoutOpeningAWorkspace()
    {
        Assert.SkipUnless(
            OperatingSystem.IsWindows(), "Framework MSBuild.exe and the non-SDK project shape are Windows-only.");
        string? msBuildExe = FindFrameworkMsBuild();
        Assert.SkipWhen(
            msBuildExe is null,
            "No Visual Studio or Build Tools install carrying MSBuild\\Current\\Bin\\MSBuild.exe was found.");

        // Arrange: a private copy of the ClassicApp fixture. No restore — a non-SDK project with only
        // framework references has no project.assets.json to produce, so restoring it only costs a process.
        using var fixture = new TempFixtureWorkspace("LegacySolutions/ClassicApp", "ClassicApp.sln", false);
        string binlog = fixture.PathOf("ClassicApp.binlog");
        RunFrameworkMsBuild(msBuildExe, fixture.SolutionPath, binlog);
        File.Exists(binlog).ShouldBeTrue($"Framework MSBuild produced no binlog at '{binlog}'.");

        // Act: the same check twice — replaying the Framework build, and opening the workspace itself.
        // --no-cache on both so neither run reads or writes persisted state and the comparison is clean.
        // Both legs run cold (no host workspace source): the LoadCount deltas below are what tell the
        // replayed leg from the built one, so each has to open, or decline to open, its own workspace.
        long loaderBeforeReplay = WorkspaceLoader.LoadCount;
        CliResult replay = await CliRunner.InvokeColdAsync(
            "check", fixture.SolutionPath, "--spec", CliRunner.ClassicAppSpecDll, "--binlog", binlog, "--no-cache");
        long replayLoads = WorkspaceLoader.LoadCount - loaderBeforeReplay;
        var replayGate = MsBuildGate.LastAcquisition;

        long loaderBeforeCold = WorkspaceLoader.LoadCount;
        CliResult cold = await CliRunner.InvokeColdAsync(
            "check", fixture.SolutionPath, "--spec", CliRunner.ClassicAppSpecDll, "--no-cache");
        long coldLoads = WorkspaceLoader.LoadCount - loaderBeforeCold;

        // Assert: byte parity across the two paths, and the replay leg never opened a workspace.
        replay.Out.ShouldBe(cold.Out);
        replay.Err.ShouldBe(cold.Err);
        replay.Exit.ShouldBe(cold.Exit);
        replay.Exit.ShouldBe(1);
        replayGate.ShouldBe(GateAcquisition.ExplicitReplay);
        replayLoads.ShouldBe(0);
        coldLoads.ShouldBe(1);
    }

    // The tool's own MSBuild choice, read back rather than re-derived. MsBuildBootstrap publishes the
    // install root it selected — whether from vswhere or from the LOADBEARING_VS_INSTALL_PATH override —
    // as VSINSTALLDIR, which is how it hands the choice to the out-of-process build host; the test process
    // has been through that path already via its module initializer. Null when no VS instance was selected
    // (the MSBuildLocator-default fallback), which the caller turns into a skip.
    private static string? FindFrameworkMsBuild()
    {
        string? vsInstallRoot = Environment.GetEnvironmentVariable("VSINSTALLDIR");
        if (string.IsNullOrWhiteSpace(vsInstallRoot)) return null;

        string msBuildExe = Path.Combine(vsInstallRoot, "MSBuild", "Current", "Bin", "MSBuild.exe");
        return File.Exists(msBuildExe) ? msBuildExe : null;
    }

    // A real .NET Framework build with the binary logger — MSBuild.exe, not `dotnet build`, so the binlog
    // is the artefact a classic build server would hand over. Node reuse is off (and the test host's
    // MSBuild/VS environment stripped) for the same reason every other child process here does it: a
    // lingering worker node inherits the redirected stdout handle and the drain never reaches EOF.
    private static void RunFrameworkMsBuild(string msBuildExe, string solutionPath, string binlogPath)
    {
        var startInfo = new ProcessStartInfo(msBuildExe)
        {
            WorkingDirectory = Path.GetDirectoryName(solutionPath)!
        };
        startInfo.ArgumentList.Add(solutionPath);
        startInfo.ArgumentList.Add($"-bl:LogFile={binlogPath}");
        startInfo.ArgumentList.Add("-nodeReuse:false");
        DotnetCli.ApplyCleanSdkEnvironment(startInfo);

        ChildProcess.ProcessResult result = ChildProcess.Run(startInfo);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"'{msBuildExe} {solutionPath}' failed with exit code {result.ExitCode}."
                + $"{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}{result.StandardError}");
    }
}