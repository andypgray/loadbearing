using System.Diagnostics;
using System.Text;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli;
using Zphil.LoadBearing.Tests.Cli;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Replay;

/// <summary>
///     The deployment-shaped regression net for the binlog-replay MSBuildLocator crash: it runs
///     the shipped <c>loadbearing.dll</c> as a <em>child process</em> so it exercises the exact condition that
///     masked the bug from the rest of the suite. Every other replay E2E drives the command tree in-process,
///     where a <c>[ModuleInitializer]</c> (<see cref="MsBuildInitializer" />) has already registered MSBuild
///     for the whole test process — so the replay path found <c>Microsoft.Build.Framework</c> regardless of
///     whether it registered the locator itself, and the deployed tool crashed while the tests stayed green.
/// </summary>
/// <remarks>
///     <para>
///         <b>What this guards.</b> The binlog reader (<c>Basic.CompilerLog.Util</c>) parses the binlog through
///         MSBuild.StructuredLogger, which binds <c>Microsoft.Build.Framework</c> at runtime. The repo ships no
///         MSBuild engine assemblies (they resolve from the SDK only after <c>MSBuildLocator</c> registration),
///         so the replay path must register the locator or the parser fails with
///         <c>Could not load file or assembly 'Microsoft.Build.Framework …'</c>. This is the only test that
///         runs the shipped registration path for replay: a fresh child process with no module initializer and
///         no pre-registration, launched with the test host's MSBuild poison vars stripped
///         (<see cref="DotnetCli.ApplyCleanSdkEnvironment" />) so the child sees a normal user environment —
///         precisely what a user typing <c>loadbearing check --binlog</c> gets.
///     </para>
///     <para>
///         One method, two legs, each costing seconds: the replay leg must exit clean with no loader error, and
///         its stdout must be byte-identical to the same <c>check</c> run without <c>--binlog</c> (a cold
///         design-time build) — byte-parity proven at the deployment boundary, not just in-process. Each leg
///         gets its own throwaway <c>LOADBEARING_CACHE_DIR</c>. In the "Serial" collection because the cold leg
///         opens a real MSBuild workspace and it shares the assembly-wide binlog fixture with the other replay
///         suites.
///     </para>
/// </remarks>
[Collection("Serial")]
public sealed class BinlogReplayDeploymentSmokeTests : IDisposable
{
    private readonly string _cacheRootBase =
        Path.Combine(Path.GetTempPath(), "loadbearing-binlog-smoke", Guid.NewGuid().ToString("N"));

    private static BinlogFixtureWorkspace Fixture => BinlogFixtureWorkspace.Instance;

    public void Dispose()
    {
        TryDeleteDirectory(_cacheRootBase);
    }

    [Fact]
    public void Check_BinlogReplayOutOfProcess_RegistersMsBuildAndMatchesColdWithoutLoaderError()
    {
        string cli = ResolveCliDll();

        // (a) the shipped condition: `check --binlog` in a child process with no pre-registered MSBuild. Before
        //     the fix this crashed here with "could not be replayed: Could not load file or assembly
        //     'Microsoft.Build.Framework …'"; the gate now registers MSBuildLocator up front, so the parser
        //     resolves the SDK's engine assemblies and the run completes clean.
        ProcessRunner.ProcessResult replay = RunCliOutOfProcess(
            cli, FreshCache(),
            "check", Fixture.SolutionPath, "--binlog", Fixture.BinlogPath, "--spec", CliRunner.CleanSpecDll);

        replay.ExitCode.ShouldBe(0);
        replay.StandardError.ShouldNotContain("could not be replayed");
        replay.StandardError.ShouldNotContain("Microsoft.Build.Framework");

        // (b) the same check WITHOUT --binlog on its own fresh cache — a cold design-time build, the parity
        //     baseline. Byte-identical stdout at the deployment boundary is the headline guarantee.
        ProcessRunner.ProcessResult cold = RunCliOutOfProcess(
            cli, FreshCache(), "check", Fixture.SolutionPath, "--spec", CliRunner.CleanSpecDll);

        cold.ExitCode.ShouldBe(0);
        replay.StandardOutput.ShouldBe(cold.StandardOutput);
    }

    // Launches `dotnet <loadbearing.dll> <args>` through the drain-safe ProcessRunner, in the same cleaned SDK
    // environment DotnetCli uses (poison MSBuild/VS vars stripped, node/server reuse off) plus a throwaway
    // LOADBEARING_CACHE_DIR — a deployment-normal child with none of the test host's MSBuild registration
    // leaking in. UTF-8 decoding matches the CLI's own console encoding so the captured text is faithful.
    private static ProcessRunner.ProcessResult RunCliOutOfProcess(string cliDll, string cacheDir, params string[] args)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = AppContext.BaseDirectory,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add(cliDll);
        foreach (string arg in args) startInfo.ArgumentList.Add(arg);

        DotnetCli.ApplyCleanSdkEnvironment(startInfo);
        startInfo.Environment[CodebaseSource.CacheDirectoryVariable] = cacheDir;

        return ProcessRunner.Run(startInfo);
    }

    private string FreshCache()
    {
        return Path.Combine(_cacheRootBase, Guid.NewGuid().ToString("N"));
    }

    // The tests project references the CLI project, so its build output (loadbearing.dll + runtimeconfig +
    // deps) is copied beside the test assembly. Fail loudly if it is not, so the test can never pass by
    // running against a stale or absent binary.
    private static string ResolveCliDll()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "loadbearing.dll");
        if (!File.Exists(path))
            throw new InvalidOperationException(
                $"The CLI build output 'loadbearing.dll' was not found beside the test assembly at '{path}'. "
                + "The tests project references Zphil.LoadBearing.Cli, so its output should be copied here.");

        return path;
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
}