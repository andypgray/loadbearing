using System.Diagnostics;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Hooks;

/// <summary>
///     Runs the committed <c>hooks/mcp-launch.sh</c> as a real child process and holds it to the four things
///     a client depends on: the server is executed from a copy outside the build tree, that copy is keyed to
///     the build it was taken from (shared while the build stands, minted afresh when it moves), a failure to
///     stage still leaves a running server, and nothing the launcher has to say ever reaches stdout.
/// </summary>
/// <remarks>
///     <para>
///         A stub <c>dotnet</c> first on <c>PATH</c> records the argument vector it was execed with and
///         returns, so the whole recipe is measured without starting a server: what is under test is which
///         copy of the CLI a client would have been connected to, not what that CLI then does.
///     </para>
///     <para>
///         The launcher runs from a working directory that is <em>not</em> the project directory and is told
///         where the build output is with a relative path, so the <c>CLAUDE_PROJECT_DIR</c> anchoring is under
///         test too: nothing can be staged at all unless the launcher changed directory first.
///     </para>
///     <para>
///         The last of the four is the one with no second chance. stdout is the JSON-RPC channel, and a single
///         stray line on it is a protocol error before the handshake — so the prelude is redirected wholesale
///         and every case here asserts an empty stdout, including the two that fail.
///     </para>
/// </remarks>
public sealed class McpLaunchScriptTests
{
    /// <summary>The file whose modification time and size key the copy, and the one the launcher execs.</summary>
    private const string ServerDll = "loadbearing.dll";

    /// <summary>A second file in the build output: proof the whole directory is staged, not just the entry point.</summary>
    private const string SiblingDll = "Zphil.LoadBearing.dll";

    private const string StagedMarker = "staged-marker.txt";

    [Fact]
    public void Launch_ExecsTheServerFromACopyOutsideTheBuildTree()
    {
        using var lab = new Lab();

        ChildProcess.ProcessResult result = lab.Launch("MyApp.sln", "--spec", "./Spec.dll");

        result.ExitCode.ShouldBe(0);
        result.StandardOutput.ShouldBeEmpty("stdout is the JSON-RPC channel and the launcher must leave it alone.");

        string[] argv = lab.ExecutedArguments();
        argv.ShouldBe(["exec", lab.StagedServerDll(), "mcp", "MyApp.sln", "--spec", "./Spec.dll"]);

        // The whole build output travels, not just the file the identity is keyed on.
        File.Exists(Path.Combine(lab.StagedDirectories().Single(), SiblingDll))
            .ShouldBeTrue($"{SiblingDll} was left behind, so the staged copy is not a runnable server.");
    }

    [Fact]
    public void SecondLaunchOnTheSameBuild_ReusesTheCopyAndRestagesNothing()
    {
        using var lab = new Lab();
        lab.Launch("MyApp.sln");
        string first = lab.ExecutedArguments()[1];

        // Written into the staged copy between the two launches: a re-stage would replace the directory this
        // sits in, so surviving is what "copied nothing" looks like from outside.
        File.WriteAllText(Path.Combine(lab.StagedDirectories().Single(), StagedMarker), "still here");

        ChildProcess.ProcessResult result = lab.Launch("MyApp.sln");

        result.ExitCode.ShouldBe(0);
        lab.ExecutedArguments()[1].ShouldBe(first);
        lab.StagedDirectories().Length.ShouldBe(
            1, "a second session on one build must share the first session's copy, not mint its own.");
        File.Exists(Path.Combine(lab.StagedDirectories().Single(), StagedMarker))
            .ShouldBeTrue("the copy was re-staged over a build that had not changed.");
    }

    [Fact]
    public void ARebuild_MintsAFreshCopyAndLeavesTheOldOneStanding()
    {
        using var lab = new Lab();
        lab.Launch("MyApp.sln");
        string beforeRebuild = lab.ExecutedArguments()[1];

        lab.WriteBuildOutput("a rebuilt CLI, of a different size");
        ChildProcess.ProcessResult result = lab.Launch("MyApp.sln");

        result.ExitCode.ShouldBe(0);
        string afterRebuild = lab.ExecutedArguments()[1];
        afterRebuild.ShouldNotBe(
            beforeRebuild, "a rebuild has to be picked up, or the client is served the previous build forever.");

        // The old directory stays: a server may still be running from it, which is the whole reason the key
        // exists rather than one directory that gets overwritten.
        lab.StagedDirectories().Length.ShouldBe(2);
        File.ReadAllText(afterRebuild).ShouldBe("a rebuilt CLI, of a different size");
    }

    [Fact]
    public void MissingBuildOutput_StopsWithAnExplanationRatherThanLaunching()
    {
        using var lab = new Lab();
        lab.BuildOutput = "not-built";

        ChildProcess.ProcessResult result = lab.Launch("MyApp.sln");

        result.ExitCode.ShouldBe(1);
        result.StandardOutput.ShouldBeEmpty();
        result.StandardError.ShouldContain("not-built/loadbearing.dll not found");
        lab.Executed.ShouldBeFalse("there was nothing to run, so nothing should have been execed.");
    }

    [Fact]
    public void WhenTheCopyCannotBeStaged_ItRunsFromTheBuildOutputAndSaysWhy()
    {
        using var lab = new Lab();

        // A run root that cannot be created, because its parent is a regular file. Whatever the reason on a
        // given machine — a full disk, a read-only home — the recipe is the same: a contended server beats no
        // server, with the reason where the client will show it.
        string file = Path.Combine(lab.Root, "not-a-directory");
        File.WriteAllText(file, "occupied");
        lab.RunRoot = Path.Combine(file, "copies");

        ChildProcess.ProcessResult result = lab.Launch("MyApp.sln");

        result.ExitCode.ShouldBe(0);
        result.StandardOutput.ShouldBeEmpty();
        result.StandardError.ShouldContain("could not stage a copy");
        lab.ExecutedArguments()[1].ShouldBe($"{lab.BuildOutput}/{ServerDll}");
    }

    /// <summary>
    ///     One launcher run's world: a project directory holding a fake build output, a run root for the
    ///     copies, and a stub <c>dotnet</c> on <c>PATH</c> that records what it was execed with.
    /// </summary>
    private sealed class Lab : IDisposable
    {
        private readonly string _argumentsFile;
        private readonly string _stubDirectory;

        internal Lab()
        {
            Root = Path.Combine(TestTempRoot.For("mcp-launcher"), Guid.NewGuid().ToString("N"));
            ProjectDirectory = Path.Combine(Root, "project");
            RunRoot = Path.Combine(Root, "copies");
            _stubDirectory = Path.Combine(Root, "stub");
            _argumentsFile = Path.Combine(Root, "execed-arguments.txt");

            Directory.CreateDirectory(Path.Combine(ProjectDirectory, BuildOutput));
            Directory.CreateDirectory(_stubDirectory);
            WriteStubDotnet();
            WriteBuildOutput("a built CLI");
        }

        internal string Root { get; }

        private string ProjectDirectory { get; }

        /// <summary>Where the copies live. Absolute, as a client would set it: the launcher never resolves it.</summary>
        internal string RunRoot { get; set; }

        /// <summary>Kept relative, so a launcher that did not anchor itself to the project finds nothing.</summary>
        internal string BuildOutput { get; set; } = "bin";

        /// <summary>Whether the launcher reached the exec at all.</summary>
        internal bool Executed => File.Exists(_argumentsFile);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The run-scoped temp root is swept by a later run; a locked file here is not a test failure.
            }
        }

        /// <summary>Writes the fake build output: the file the copy is keyed on, and one sibling beside it.</summary>
        internal void WriteBuildOutput(string serverDllContent)
        {
            string directory = Path.Combine(ProjectDirectory, BuildOutput);
            File.WriteAllText(Path.Combine(directory, ServerDll), serverDllContent);
            File.WriteAllText(Path.Combine(directory, SiblingDll), "a dependency of the server");
        }

        /// <summary>Runs the committed launcher with <paramref name="serverArguments" /> to pass through.</summary>
        internal ChildProcess.ProcessResult Launch(params string[] serverArguments)
        {
            string shell = ShellInterpreter.Locate(ShellInterpreter.Sh) ?? string.Empty;
            Assert.SkipWhen(
                shell.Length == 0,
                "'sh' is not available on this machine, so the launcher cannot be run here. It runs wherever a "
                + "POSIX shell is installed, which is every CI OS.");

            File.Delete(_argumentsFile);

            var startInfo = new ProcessStartInfo(shell)
            {
                // Not the project directory: the launcher has to reach CLAUDE_PROJECT_DIR itself.
                WorkingDirectory = Root
            };
            startInfo.ArgumentList.Add(Posix(Path.Combine(RepoRoot.Directory, "hooks", "mcp-launch.sh")));
            foreach (string argument in serverArguments) startInfo.ArgumentList.Add(argument);

            startInfo.Environment["PATH"] =
                _stubDirectory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
            startInfo.Environment["CLAUDE_PROJECT_DIR"] = Posix(ProjectDirectory);
            startInfo.Environment["LOADBEARING_MCP_BUILD_OUTPUT"] = BuildOutput;
            startInfo.Environment["LOADBEARING_MCP_RUN_ROOT"] = Posix(RunRoot);
            startInfo.Environment["LOADBEARING_TEST_EXECED_ARGUMENTS"] = Posix(_argumentsFile);

            return ChildProcess.Run(startInfo, TimeSpan.FromMinutes(1));
        }

        /// <summary>What the stub <c>dotnet</c> was execed with, one argument per line.</summary>
        internal string[] ExecutedArguments()
        {
            File.Exists(_argumentsFile).ShouldBeTrue("the launcher never reached the exec.");

            return File.ReadAllLines(_argumentsFile);
        }

        /// <summary>The copies staged so far, newest key last.</summary>
        internal string[] StagedDirectories()
        {
            return Directory.Exists(RunRoot) ? Directory.GetDirectories(RunRoot).Order().ToArray() : [];
        }

        /// <summary>The server the launcher should have execed: the one file inside the single staged copy.</summary>
        internal string StagedServerDll()
        {
            return Posix(Path.Combine(StagedDirectories().Single(), ServerDll));
        }

        /// <summary>
        ///     Writes the stub <c>dotnet</c>: an extensionless shebang script, which is what a POSIX shell
        ///     resolves on every OS. It records its argument vector and exits, so no server is ever started.
        /// </summary>
        private void WriteStubDotnet()
        {
            string stub = Path.Combine(_stubDirectory, "dotnet");
            File.WriteAllText(
                stub,
                "#!/bin/sh\n"
                + "printf '%s\\n' \"$@\" > \"$LOADBEARING_TEST_EXECED_ARGUMENTS\"\n");

            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(
                    stub,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        // Forward slashes throughout: a POSIX shell takes C:/... as a path, while a backslash inside it is an
        // escape character rather than a separator.
        private static string Posix(string path)
        {
            return path.Replace('\\', '/');
        }
    }
}