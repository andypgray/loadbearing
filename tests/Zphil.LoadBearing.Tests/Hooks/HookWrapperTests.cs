using System.Diagnostics;
using System.Globalization;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Hooks;

/// <summary>
///     Runs the committed hook wrappers as real child processes and holds them to the contract they
///     publish: the exit-code mapping a Claude Code hook depends on (clean → 0, a red rule → 2 with the
///     report on stderr, LoadBearing's own error → 1 under a <c>loadbearing config error:</c> prefix) and
///     the PostToolUse payload filter that keeps a docs edit from paying for a check. A stub
///     <c>loadbearing</c> first on <c>PATH</c> supplies the exit code and records that it ran, so the
///     mapping is measured without a workspace load.
/// </summary>
/// <remarks>
///     <para>
///         The wrapper is invoked from a working directory that is <em>not</em> the project directory, so
///         the <c>CLAUDE_PROJECT_DIR</c> anchoring is exercised too: the stub's sentinel can only land
///         where the assertions look for it if the wrapper changed directory first.
///     </para>
///     <para>
///         The <c>sh</c> arm is the gate proper — every CI OS has a POSIX shell, Git Bash supplying it on
///         Windows. The <c>pwsh</c> arm runs the same five cases where PowerShell 7 is installed and skips
///         with a named reason where it is not, which is the case on the maintainer's machine (and why the
///         local hook install uses the <c>.sh</c> variant at all).
///     </para>
///     <para>
///         The twin gate at the bottom is the other half: five wrappers ship the same contract region, and
///         a fix applied to one of them and not the rest would otherwise drift silently.
///     </para>
/// </remarks>
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

    /// <summary>The line every wrapper's contract region opens with — verified byte-identical in all five.</summary>
    private const string ContractRegionStart =
        "# Hooks run in the session's current directory, which need not be the repository root;";

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
        Lines(run.Result.StandardError).ShouldContain(CannedReport);
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
        Lines(run.Result.StandardError).ShouldStartWith("loadbearing config error:");
    }

    [Theory]
    [InlineData(Sh)]
    [InlineData(Pwsh)]
    public void NonCodePayload_SkipsTheCheckEntirely(string interpreter)
    {
        HookRun run = Fire(interpreter, DocsEditPayload, stubExit: 0);

        run.Result.ExitCode.ShouldBe(0);
        File.Exists(Path.Combine(run.ProjectDirectory, Sentinel))
            .ShouldBeFalse("A docs edit must not pay for a check — the wrapper's payload filter should have "
                           + "returned before invoking loadbearing at all.");
    }

    [Theory]
    [InlineData(Sh)]
    [InlineData(Pwsh)]
    public void CodePayload_RunsTheCheck(string interpreter)
    {
        HookRun run = Fire(interpreter, CodeEditPayload, stubExit: 0);

        run.Result.ExitCode.ShouldBe(0);
        File.Exists(Path.Combine(run.ProjectDirectory, Sentinel))
            .ShouldBeTrue("A .cs edit must reach the check, and the sentinel only lands if the wrapper both "
                          + "invoked loadbearing and changed to CLAUDE_PROJECT_DIR first.");
    }

    [Theory]
    [InlineData(Sh)]
    [InlineData(Pwsh)]
    public void CommittedWrappers_ShareOneContractRegion(string family)
    {
        string[] wrappers = family == Sh ? CommittedShWrappers : CommittedPowerShellWrappers;
        string reference = ContractRegion(wrappers[0]);

        foreach (string wrapper in wrappers.Skip(1))
            ContractRegion(wrapper).ShouldBe(
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

        ContractRegion(".claude/arch-hook.sh").ShouldBe(
            ContractRegion("hooks/arch-hook.sh"),
            ".claude/arch-hook.sh has drifted from hooks/arch-hook.sh inside the wrapper contract region. "
            + "The local install may only differ from the committed wrapper on its invocation line (dotnet "
            + "exec on the built HEAD CLI rather than the installed global tool).");
    }

    /// <summary>
    ///     The wrapper contract region of <paramref name="repoRelativePath" />: the shared opening line to end
    ///     of file, with the two per-repo differences flattened. Header prose above the region is per-repo by
    ///     design and stays out of the comparison.
    /// </summary>
    private static string ContractRegion(string repoRelativePath)
    {
        string path = Path.Combine(RepoRoot.Directory, repoRelativePath.Replace('/', Path.DirectorySeparatorChar));
        string[] lines = File.ReadAllLines(path);
        int start = Array.IndexOf(lines, ContractRegionStart);
        start.ShouldBeGreaterThanOrEqualTo(
            0, $"{repoRelativePath} does not carry the wrapper contract region's opening line.");

        return string.Join('\n', lines.Skip(start).Select(NormaliseLine));
    }

    /// <summary>
    ///     Flattens the two things a wrapper is allowed to fill in for its own repository: what it defaults
    ///     the three config values to, and how it invokes the tool (installed global tool, or
    ///     <c>dotnet exec</c> on a local build). Everything else must match byte for byte.
    /// </summary>
    private static string NormaliseLine(string line)
    {
        if (ShConfigKeys.Any(key => line.StartsWith(key, StringComparison.Ordinal))) return CutAfter(line, ":-") + "<value>}\"";

        if (PowerShellConfigKeys.Any(key => line.StartsWith(key, StringComparison.Ordinal))) return CutAfter(line, "else { '") + "<value>' }";

        bool isInvocation = line.StartsWith("out=$(", StringComparison.Ordinal)
                            || line.StartsWith("$out = ", StringComparison.Ordinal);

        return isInvocation ? InvocationPlaceholder : line;
    }

    private static string CutAfter(string line, string marker)
    {
        int index = line.IndexOf(marker, StringComparison.Ordinal);
        index.ShouldBeGreaterThanOrEqualTo(0, $"Expected '{marker}' in the wrapper config line: {line}");

        return line[..(index + marker.Length)];
    }

    /// <summary>
    ///     Runs <c>hooks/arch-hook.{sh,ps1}</c> under <paramref name="interpreter" /> against a stub
    ///     <c>loadbearing</c> that exits <paramref name="stubExit" />, feeding <paramref name="payload" /> on
    ///     stdin as Claude Code's PostToolUse hook does.
    /// </summary>
    private static HookRun Fire(string interpreter, string payload, int stubExit)
    {
        string interpreterPath = RequireInterpreter(interpreter);
        string root = Path.Combine(TestTempRoot.For("hook-wrappers"), Guid.NewGuid().ToString("N"));
        string projectDirectory = Path.Combine(root, "project");
        string stubDirectory = Path.Combine(root, "stub");
        Directory.CreateDirectory(projectDirectory);
        Directory.CreateDirectory(stubDirectory);
        WriteStub(stubDirectory);

        string wrapper = Path.Combine(
            RepoRoot.Directory, "hooks", interpreter == Sh ? "arch-hook.sh" : "arch-hook.ps1");
        var startInfo = new ProcessStartInfo(interpreterPath)
        {
            // Deliberately not the project directory: the wrapper has to cd to CLAUDE_PROJECT_DIR itself.
            WorkingDirectory = root
        };
        if (interpreter == Sh)
        {
            // Forward slashes: a POSIX shell on Windows takes C:/... without the backslash-escaping question.
            startInfo.ArgumentList.Add(wrapper.Replace('\\', '/'));
        }
        else
        {
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(wrapper);
        }

        startInfo.Environment["PATH"] =
            stubDirectory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        startInfo.Environment["CLAUDE_PROJECT_DIR"] = projectDirectory.Replace('\\', '/');
        startInfo.Environment["STUB_EXIT"] = stubExit.ToString(CultureInfo.InvariantCulture);

        // The payload goes in on stdin and the pipe closes behind it — the same closed stdin every child
        // here gets, which is also what makes a wrapper's "is stdin a terminal?" branch deterministic
        // under a test runner.
        ChildProcess.ProcessResult result =
            ChildProcess.Run(startInfo, TimeSpan.FromMinutes(1), payload);

        return new HookRun(result, projectDirectory);
    }

    /// <summary>
    ///     Writes the stub <c>loadbearing</c> in both shapes a wrapper can reach: an extensionless
    ///     shebang script (what a POSIX shell resolves, on every OS) and a <c>.cmd</c> (what PowerShell
    ///     resolves on Windows, where an extensionless file is not an executable).
    /// </summary>
    private static void WriteStub(string stubDirectory)
    {
        string posix = Path.Combine(stubDirectory, "loadbearing");
        File.WriteAllText(
            posix,
            "#!/bin/sh\n"
            + $"printf 'ran\\n' > {Sentinel}\n"
            + "printf 'FAIL arch/stub-rule -- canned violation report\\n'\n"
            + "printf '  Probe.cs:10 -- second report line, so the multi-line path is covered\\n'\n"
            + "exit \"${STUB_EXIT:-0}\"\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(
                posix,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        // echo( is the batch form that echoes leading whitespace verbatim, which the report's second line needs.
        File.WriteAllText(
            Path.Combine(stubDirectory, "loadbearing.cmd"),
            "@echo off\r\n"
            + $"echo ran>{Sentinel}\r\n"
            + "echo(FAIL arch/stub-rule -- canned violation report\r\n"
            + "echo(  Probe.cs:10 -- second report line, so the multi-line path is covered\r\n"
            + "exit /b %STUB_EXIT%\r\n");
    }

    /// <summary>
    ///     Resolves <paramref name="interpreter" />, and skips the test with a named reason where it is not
    ///     installed.
    /// </summary>
    private static string RequireInterpreter(string interpreter)
    {
        string path = ShellInterpreter.Locate(interpreter) ?? string.Empty;
        Assert.SkipWhen(
            path.Length == 0,
            $"'{interpreter}' is not available on this machine, so the {interpreter} wrapper arm cannot run "
            + "here. It runs wherever the interpreter is installed, which for sh is every CI OS.");

        return path;
    }

    /// <summary>Line endings only: the wrappers write LF, the Windows shells that host them write CRLF.</summary>
    private static string Lines(string text)
    {
        return text.Replace("\r\n", "\n");
    }

    private readonly record struct HookRun(ChildProcess.ProcessResult Result, string ProjectDirectory);
}
