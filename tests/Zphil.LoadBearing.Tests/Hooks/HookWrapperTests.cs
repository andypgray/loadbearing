using System.Diagnostics;
using System.Globalization;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Roslyn.Hosting;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Hooks;

/// <summary>
///     Runs the committed hook wrappers as real child processes and holds them to the contract they
///     publish: the exit-code mapping a Claude Code hook depends on (clean → 0, a red rule → 2 with the
///     report on stderr, LoadBearing's own error → 1 under a <c>loadbearing config error:</c> prefix), the
///     PostToolUse payload filter that keeps a docs edit from paying for a check, and the working-tree
///     guard that spends a check only on the tree the edit actually landed in. A stub <c>loadbearing</c>
///     first on <c>PATH</c> supplies the exit code and records that it ran, so the mapping is measured
///     without a workspace load.
/// </summary>
/// <remarks>
///     <para>
///         The wrapper is invoked from a working directory that is <em>not</em> the project directory
///         unless a case names one, so the <c>CLAUDE_PROJECT_DIR</c> anchoring is exercised too: the stub's
///         sentinel can only land where the assertions look for it if the wrapper changed directory first.
///     </para>
///     <para>
///         The working-tree rows build a real git repository with a real linked worktree under
///         <c>.claude/worktrees/</c>, which is where Claude Code puts one. Nothing cheaper reproduces the
///         shape: a worktree is inside the project directory <em>by path</em> and a separate working tree
///         <em>by git</em>, and telling those apart is the whole of what the guard does.
///     </para>
///     <para>
///         The <c>sh</c> arm is the gate proper — every CI OS has a POSIX shell, Git Bash supplying it on
///         Windows. The <c>pwsh</c> arm runs the same eleven cases where PowerShell 7 is installed and
///         skips with a named reason where it is not, which is the case on the maintainer's machine (and
///         why the local hook install uses the <c>.sh</c> variant at all).
///     </para>
///     <para>
///         The twin gate at the bottom is the other half: five wrappers ship the same contract region, and
///         a fix applied to one of them and not the rest would otherwise drift silently.
///     </para>
/// </remarks>
[Collection("Serial")]
public sealed class HookWrapperTests
{
    private const string Sh = ShellInterpreter.Sh;
    private const string Pwsh = ShellInterpreter.Pwsh;

    /// <summary>The stub's canned report — two lines, so the multi-line path through each wrapper is covered.</summary>
    private const string CannedReport =
        "FAIL arch/stub-rule -- canned violation report\n"
        + "  Probe.cs:10 -- second report line, so the multi-line path is covered";

    /// <summary>Written by the stub into the project directory: proof that the check was actually invoked.</summary>
    private const string Sentinel = "loadbearing-ran.txt";

    private const string CodeEditPayload = """{"tool_input":{"file_path":"src/Zphil.LoadBearing/Probe.cs"}}""";
    private const string DocsEditPayload = """{"tool_input":{"file_path":"docs/notes.md"}}""";

    /// <summary>
    ///     The line every wrapper's contract region opens with — verified byte-identical in all five. It is
    ///     the payload read rather than the <c>cd</c> below it because everything that decides whether to
    ///     spend a check has to happen first, so the guards are inside the region the twin gate compares.
    /// </summary>
    private const string ContractRegionStart =
        "# The PostToolUse payload on stdin names the edited file. Read it once, because stdin does not";

    private const string InvocationPlaceholder = "<invocation>";

    private static readonly string[] ShConfigKeys = ["SOLUTION=", "SPEC=", "DIFF_BASE="];

    private static readonly string[] PowerShellConfigKeys = ["$Solution ", "$Spec ", "$DiffBase "];

    private static readonly string[] CommittedShWrappers =
        ["hooks/arch-hook.sh", "examples/Meridian/hooks/arch-hook.sh"];

    private static readonly string[] CommittedPowerShellWrappers =
        ["hooks/arch-hook.ps1", "examples/Meridian/hooks/arch-hook.ps1"];

    [Theory]
    [InlineData(Sh)]
    [InlineData(Pwsh)]
    public void CleanCheck_ProceedsWithExitZeroAndNoStderr(string interpreter)
    {
        HookRun run = Fire(interpreter, payload: "", stubExit: 0);

        run.Result.ExitCode.ShouldBe(0);
        run.Result.StandardError.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(Sh)]
    [InlineData(Pwsh)]
    public void RedRule_BlocksWithExitTwoAndTheReportOnStderr(string interpreter)
    {
        HookRun run = Fire(interpreter, payload: "", stubExit: 1);

        // 2 is how a Claude Code hook blocks, and the report on stderr is what the agent reads to self-correct.
        run.Result.ExitCode.ShouldBe(2);
        run.Result.StandardError.NormalizedLines()
            .ShouldContain(CannedReport);
    }

    [Theory]
    [InlineData(Sh)]
    [InlineData(Pwsh)]
    public void ToolError_SurfacesAsExitOneUnderTheConfigErrorPrefix(string interpreter)
    {
        HookRun run = Fire(interpreter, payload: "", stubExit: 2);

        // Not 2: LoadBearing's own error is a config problem the user sees, not a violation the agent is
        // told to "fix".
        run.Result.ExitCode.ShouldBe(1);
        run.Result.StandardError.NormalizedLines()
            .ShouldStartWith("loadbearing config error:");
    }

    [Theory]
    [InlineData(Sh)]
    [InlineData(Pwsh)]
    public void NonCodePayload_SkipsTheCheckEntirely(string interpreter)
    {
        HookRun run = Fire(interpreter, DocsEditPayload, stubExit: 0);

        run.Result.ExitCode.ShouldBe(0);
        ShouldNotHaveRunIn(run.ProjectDirectory,
            "A docs edit must not pay for a check — the wrapper's payload filter should have returned "
            + "before invoking loadbearing at all.");
    }

    [Theory]
    [InlineData(Sh)]
    [InlineData(Pwsh)]
    public void CodePayload_RunsTheCheck(string interpreter)
    {
        // The payload path is relative, which is the fallback the guard reads as project-relative.
        HookRun run = Fire(interpreter, CodeEditPayload, stubExit: 0);

        run.Result.ExitCode.ShouldBe(0);
        ShouldHaveRunIn(run.ProjectDirectory,
            "A .cs edit must reach the check, and the sentinel only lands if the wrapper both invoked "
            + "loadbearing and changed to CLAUDE_PROJECT_DIR first.");
    }

    [Theory]
    [InlineData(Sh)]
    [InlineData(Pwsh)]
    public void AbsolutePayloadInsideTheProjectDirectory_RunsTheCheck(string interpreter)
    {
        HookSandbox sandbox = Arrange();
        // A real repository, because this is the everyday path: both sides resolve to the same working
        // tree and the run proceeds. Without one they resolve to nothing, which proceeds by fallback and
        // would pass just as well with the comparison inverted.
        InitialiseRepository(sandbox);
        string edited = Path.Combine(sandbox.ProjectDirectory, "src", "Probe.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(edited)!);

        HookRun run = Fire(interpreter, sandbox, PayloadNaming(edited), stubExit: 0);

        run.Result.ExitCode.ShouldBe(0);
        ShouldHaveRunIn(run.ProjectDirectory,
            "This is the payload shape Claude Code actually sends: an absolute path, JSON-escaped, inside "
            + "the project directory and in its working tree. Nothing about it should stop the check.");
    }

    [Theory]
    [InlineData(Sh)]
    [InlineData(Pwsh)]
    public void PayloadOutsideTheProjectDirectory_SkipsTheCheck(string interpreter)
    {
        HookSandbox sandbox = Arrange();
        string edited = Path.Combine(sandbox.Root, "elsewhere", "Probe.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(edited)!);

        HookRun run = Fire(interpreter, sandbox, PayloadNaming(edited), stubExit: 0);

        run.Result.ExitCode.ShouldBe(0);
        ShouldNotHaveRunIn(run.ProjectDirectory,
            "An edit outside CLAUDE_PROJECT_DIR must not buy a check of the project: the verdict would be "
            + "about code the edit never touched. This is also the leg that covers a file in no repository "
            + "at all, a scratch directory being the everyday case.");
    }

    [Theory]
    [InlineData(Sh)]
    [InlineData(Pwsh)]
    public void PayloadInALinkedWorktree_SkipsTheCheck(string interpreter)
    {
        HookSandbox sandbox = Arrange();
        InitialiseRepository(sandbox);
        string worktree = AddLinkedWorktree(sandbox);

        HookRun run = Fire(interpreter, sandbox, PayloadNaming(Path.Combine(worktree, "Probe.cs")), stubExit: 0);

        run.Result.ExitCode.ShouldBe(0);
        ShouldNotHaveRunIn(run.ProjectDirectory,
            "A session in the main checkout writing to a worktree by absolute path is inside the project "
            + "directory by path and in a different working tree by git. Only the second answer is the "
            + "right one, so the check must not run.");
    }

    [Theory]
    [InlineData(Sh)]
    [InlineData(Pwsh)]
    public void SessionInsideALinkedWorktree_SkipsTheCheck(string interpreter)
    {
        HookSandbox sandbox = Arrange();
        InitialiseRepository(sandbox);
        string worktree = AddLinkedWorktree(sandbox);

        HookRun run = Fire(interpreter, sandbox, payload: "", stubExit: 0, workingDirectory: worktree);

        run.Result.ExitCode.ShouldBe(0);
        ShouldNotHaveRunIn(run.ProjectDirectory,
            "With no payload the current directory stands in for the edited file, so a hand-run inside a "
            + "worktree opts itself out rather than checking the main checkout CLAUDE_PROJECT_DIR names.");
    }

    [Theory]
    [InlineData(Sh)]
    [InlineData(Pwsh)]
    public void SessionInASubdirectoryOfTheProject_RunsTheCheck(string interpreter)
    {
        HookSandbox sandbox = Arrange();
        InitialiseRepository(sandbox);
        string subdirectory = Path.Combine(sandbox.ProjectDirectory, "src");
        Directory.CreateDirectory(subdirectory);

        HookRun run = Fire(interpreter, sandbox, payload: "", stubExit: 0, workingDirectory: subdirectory);

        run.Result.ExitCode.ShouldBe(0);
        ShouldHaveRunIn(run.ProjectDirectory,
            "A session below the repository root is in the project's own working tree and must be checked. "
            + "Asking git which tree each side is in answers that identically from any depth, which a "
            + "comparison of git's own directory spellings does not.");
    }

    [Theory]
    [InlineData(Sh)]
    [InlineData(Pwsh)]
    public void NoProjectDirectory_StillChecks(string interpreter)
    {
        HookRun run = Fire(interpreter, Arrange(), payload: "", stubExit: 0, withProjectDirectory: false);

        run.Result.ExitCode.ShouldBe(0);
        ShouldHaveRunIn(run.WorkingDirectory,
            "A hand-run outside Claude Code has no CLAUDE_PROJECT_DIR to compare anything against, so the "
            + "guard steps aside and the check runs where the run was launched.");
    }

    [Theory]
    [InlineData(Sh)]
    [InlineData(Pwsh)]
    public void CommittedWrappers_ShareOneContractRegion(string family)
    {
        string[] wrappers = family == Sh ? CommittedShWrappers : CommittedPowerShellWrappers;
        string reference = ShouldHaveContractRegion(wrappers[0]);

        foreach (string wrapper in wrappers.Skip(1))
            ShouldHaveContractRegion(wrapper)
                .ShouldBe(
                    reference,
                    $"{wrapper} has drifted from {wrappers[0]} inside the wrapper contract region. Only the three "
                    + "config defaults and the invocation line may differ between wrappers; everything from "
                    + $"\"{ContractRegionStart}\" to end of file is one shared contract.");
    }

    [Fact]
    public void LiveWrapper_SharesTheCommittedContractRegion()
    {
        // Not committed (.claude/ is a local dev aid), so this arm covers less on a fresh clone and in CI —
        // said out loud rather than passing quietly, because a gate that silently shrinks is the failure mode.
        Assert.SkipUnless(
            File.Exists(Path.Combine(RepoRoot.Directory, ".claude", "arch-hook.sh")),
            ".claude/arch-hook.sh is absent — this checkout carries no local hook install, so the live "
            + "wrapper is outside the twin comparison on this run.");

        ShouldHaveContractRegion(".claude/arch-hook.sh")
            .ShouldBe(
                ShouldHaveContractRegion("hooks/arch-hook.sh"),
                ".claude/arch-hook.sh has drifted from hooks/arch-hook.sh inside the wrapper contract region. "
                + "The local install may only differ from the committed wrapper on its invocation line (dotnet "
                + "exec on the built HEAD CLI rather than the installed global tool).");
    }

    /// <summary>
    ///     The wrapper contract region of <paramref name="repoRelativePath" />: the shared opening line to end
    ///     of file, with the two per-repo differences flattened. Header prose above the region is per-repo by
    ///     design and stays out of the comparison.
    /// </summary>
    private static string ShouldHaveContractRegion(string repoRelativePath)
    {
        string path = Path.Combine(RepoRoot.Directory, repoRelativePath.Replace('/', Path.DirectorySeparatorChar));
        string[] lines = File.ReadAllLines(path);
        int start = Array.IndexOf(lines, ContractRegionStart);
        start.ShouldBeGreaterThanOrEqualTo(
            0, $"{repoRelativePath} does not carry the wrapper contract region's opening line.");

        return string.Join('\n', lines.Skip(start)
            .Select(NormaliseLine));
    }

    /// <summary>
    ///     Flattens the two things a wrapper is allowed to fill in for its own repository: what it defaults
    ///     the three config values to, and how it invokes the tool (installed global tool, or
    ///     <c>dotnet exec</c> on a local build). Everything else must match byte for byte.
    /// </summary>
    private static string NormaliseLine(string line)
    {
        if (ShConfigKeys.Any(key => line.StartsWith(key, StringComparison.Ordinal))) return ShouldCutAfter(line, ":-") + "<value>}\"";

        if (PowerShellConfigKeys.Any(key => line.StartsWith(key, StringComparison.Ordinal))) return ShouldCutAfter(line, "else { '") + "<value>' }";

        bool isInvocation = line.StartsWith("out=$(", StringComparison.Ordinal)
                            || line.StartsWith("$out = ", StringComparison.Ordinal);

        return isInvocation ? InvocationPlaceholder : line;
    }

    private static string ShouldCutAfter(string line, string marker)
    {
        int index = line.IndexOf(marker, StringComparison.Ordinal);
        index.ShouldBeGreaterThanOrEqualTo(0, $"Expected '{marker}' in the wrapper config line: {line}");

        return line[..(index + marker.Length)];
    }

    /// <summary>
    ///     Runs <c>hooks/arch-hook.{sh,ps1}</c> under <paramref name="interpreter" /> against a fresh
    ///     sandbox — the shape the rows that need no repository underneath them want.
    /// </summary>
    private static HookRun Fire(string interpreter, string payload, int stubExit)
    {
        return Fire(interpreter, Arrange(), payload, stubExit);
    }

    /// <summary>
    ///     Runs <c>hooks/arch-hook.{sh,ps1}</c> under <paramref name="interpreter" /> against
    ///     <paramref name="sandbox" />'s stub <c>loadbearing</c>, which exits <paramref name="stubExit" />,
    ///     feeding <paramref name="payload" /> on stdin as Claude Code's PostToolUse hook does.
    /// </summary>
    /// <remarks>
    ///     Two optional knobs carry the shapes the guard has to tell apart.
    ///     <paramref name="workingDirectory" /> is where the wrapper is launched from, defaulting to the
    ///     sandbox root, which is <em>not</em> the project directory — so every row also proves the
    ///     <c>CLAUDE_PROJECT_DIR</c> anchoring, and the rows that care where the session sits name a
    ///     directory of their own. <paramref name="withProjectDirectory" /> decides whether
    ///     <c>CLAUDE_PROJECT_DIR</c> is set at all: false is the hand-run outside Claude Code, and the
    ///     variable is removed rather than left unset, because the child inherits this process's
    ///     environment.
    /// </remarks>
    private static HookRun Fire(
        string interpreter,
        HookSandbox sandbox,
        string payload,
        int stubExit,
        string? workingDirectory = null,
        bool withProjectDirectory = true)
    {
        string interpreterPath = ShellInterpreter.Require(interpreter);
        string launchDirectory = workingDirectory ?? sandbox.Root;

        string wrapper = Path.Combine(
            RepoRoot.Directory, "hooks", interpreter == Sh ? "arch-hook.sh" : "arch-hook.ps1");
        var startInfo = new ProcessStartInfo(interpreterPath)
        {
            WorkingDirectory = launchDirectory
        };
        if (interpreter == Sh)
        {
            // Forward slashes: a POSIX shell on Windows takes C:/... without the backslash-escaping question.
            startInfo.ArgumentList.Add(ShellInterpreter.Posix(wrapper));
        }
        else
        {
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(wrapper);
        }

        startInfo.Environment["PATH"] = ShellInterpreter.PrependedPath(sandbox.StubDirectory);
        startInfo.Environment["STUB_EXIT"] = stubExit.ToString(CultureInfo.InvariantCulture);
        if (withProjectDirectory)
            startInfo.Environment["CLAUDE_PROJECT_DIR"] = ShellInterpreter.Posix(sandbox.ProjectDirectory);
        else
            startInfo.Environment.Remove("CLAUDE_PROJECT_DIR");

        // The payload goes in on stdin and the pipe closes behind it — the same closed stdin every child
        // here gets, which is also what makes a wrapper's "is stdin a terminal?" branch deterministic
        // under a test runner.
        ChildProcess.ProcessResult result =
            ChildProcess.Run(startInfo, TimeSpan.FromMinutes(1), payload);

        return new HookRun(result, sandbox.ProjectDirectory, launchDirectory);
    }

    /// <summary>
    ///     A throwaway project directory with a stub <c>loadbearing</c> beside it: what one wrapper run is
    ///     fired against. Minting it apart from the run is what lets a row build a git repository, or a
    ///     linked worktree, inside it before the wrapper ever sees it.
    /// </summary>
    private static HookSandbox Arrange()
    {
        string root = Path.Combine(TestTempRoot.For("hook-wrappers"), Guid.NewGuid()
            .ToString("N"));
        string projectDirectory = Path.Combine(root, "project");
        string stubDirectory = Path.Combine(root, "stub");
        Directory.CreateDirectory(projectDirectory);
        Directory.CreateDirectory(stubDirectory);
        WriteStub(stubDirectory);

        return new HookSandbox(root, projectDirectory, stubDirectory);
    }

    /// <summary>
    ///     Makes the sandbox's project directory a git repository with one empty commit — enough for a
    ///     working tree to exist and for a worktree to be added, and nothing more.
    /// </summary>
    private static void InitialiseRepository(HookSandbox sandbox)
    {
        GitCommand.InitRepository(sandbox.ProjectDirectory);
        GitCommand.Run(
            sandbox.ProjectDirectory, "-c", "commit.gpgsign=false", "commit", "--allow-empty", "-m", "baseline");
    }

    /// <summary>
    ///     Adds a linked worktree under <c>.claude/worktrees/</c>, which is where Claude Code puts one and
    ///     therefore the only placement that reproduces the shape: inside the project directory by path,
    ///     and a working tree of its own by git.
    /// </summary>
    private static string AddLinkedWorktree(HookSandbox sandbox)
    {
        string worktree = Path.Combine(sandbox.ProjectDirectory, ".claude", "worktrees", "w1");
        GitCommand.Run(sandbox.ProjectDirectory, "worktree", "add", worktree);

        return worktree;
    }

    /// <summary>
    ///     A PostToolUse payload naming <paramref name="path" />, escaped the way JSON escapes it — which on
    ///     Windows doubles every separator, the spelling the wrapper has to undo before it can compare the
    ///     path to anything.
    /// </summary>
    private static string PayloadNaming(string path)
    {
        string escaped = path.Replace("\\", "\\\\");

        // Three dollars, because the payload's own closing braces are the last thing in the literal.
        return $$$"""{"tool_input":{"file_path":"{{{escaped}}}"}}""";
    }

    private static void ShouldHaveRunIn(string directory, string because)
    {
        File.Exists(Path.Combine(directory, Sentinel))
            .ShouldBeTrue(because);
    }

    private static void ShouldNotHaveRunIn(string directory, string because)
    {
        File.Exists(Path.Combine(directory, Sentinel))
            .ShouldBeFalse(because);
    }

    /// <summary>
    ///     Writes the stub <c>loadbearing</c> the wrappers resolve off <c>PATH</c>: it records that it ran,
    ///     prints the canned two-line report, and exits <c>STUB_EXIT</c>.
    /// </summary>
    private static void WriteStub(string stubDirectory)
    {
        ShellInterpreter.WriteExecutableStub(
            stubDirectory,
            "loadbearing",
            "#!/bin/sh\n"
            + $"printf 'ran\\n' > {Sentinel}\n"
            + "printf 'FAIL arch/stub-rule -- canned violation report\\n'\n"
            + "printf '  Probe.cs:10 -- second report line, so the multi-line path is covered\\n'\n"
            + "exit \"${STUB_EXIT:-0}\"\n",
            // echo( is the batch form that echoes leading whitespace verbatim, which the report's second line needs.
            "@echo off\r\n"
            + $"echo ran>{Sentinel}\r\n"
            + "echo(FAIL arch/stub-rule -- canned violation report\r\n"
            + "echo(  Probe.cs:10 -- second report line, so the multi-line path is covered\r\n"
            + "exit /b %STUB_EXIT%\r\n");
    }

    private readonly record struct HookSandbox(string Root, string ProjectDirectory, string StubDirectory);

    private readonly record struct HookRun(
        ChildProcess.ProcessResult Result,
        string ProjectDirectory,
        string WorkingDirectory);
}
