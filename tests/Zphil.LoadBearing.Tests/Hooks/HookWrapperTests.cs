using System.Diagnostics;
using System.Globalization;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Roslyn.Hosting;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Hooks;

/// <summary>
///     Runs the committed hook wrappers as real child processes and holds them to the contract they
///     publish: the exit-code mapping a Claude Code hook depends on (clean → 0 with whatever the check
///     wrote passed through on stdout, a red rule → 2 with the report on stderr, LoadBearing's own error →
///     1 under a <c>loadbearing config error:</c> prefix), the turn-end behaviour a <c>Stop</c> payload
///     buys — the unchanged-tree skip, the per-prompt round cap, the worktree the session is actually in —
///     the PostToolUse payload filter that keeps a docs edit from paying for a check, and the working-tree
///     guard that spends a check only on the tree the edit actually landed in. A stub <c>loadbearing</c>
///     first on <c>PATH</c> supplies the exit code and the output, and records that it ran, so the mapping
///     is measured without a workspace load.
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
///         Every row runs with <c>LOADBEARING_HOOK_STATE_DIR</c> inside its own sandbox, so a turn-end
///         verdict is written where the row can read it back and never into the machine's temp root. The
///         skip rows are the ones that need a real repository underneath them for a second reason: the
///         verdict records the HEAD and the changed-code listing it covered, and a skip is exactly the
///         claim that both still hold.
///     </para>
///     <para>
///         The <c>sh</c> arm is the gate proper — every CI OS has a POSIX shell, Git Bash supplying it on
///         Windows. The <c>pwsh</c> arm runs the same cases where PowerShell 7 is installed and
///         skips with a named reason where it is not — the shape that keeps the arm portable across
///         machines that have PowerShell 7 and machines that do not.
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
    internal const string Sentinel = "loadbearing-ran.txt";

    private const string CodeEditPayload = """{"tool_input":{"file_path":"src/Zphil.LoadBearing/Probe.cs"}}""";
    private const string DocsEditPayload = """{"tool_input":{"file_path":"docs/notes.md"}}""";

    /// <summary>
    ///     The line every wrapper's contract region opens with — verified byte-identical in all five. It is
    ///     the payload read rather than the <c>cd</c> below it because everything that decides whether to
    ///     spend a check has to happen first, so the guards are inside the region the twin gate compares.
    /// </summary>
    private const string ContractRegionStart =
        "# The hook payload on stdin says which event fired and what it touched. Read it whole and once,";

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
    public void CleanCheck_ProceedsWithExitZeroAndTheCheckOutputOnStdout(string interpreter)
    {
        HookRun run = Fire(interpreter, payload: "", stubExit: 0);

        run.ShouldExitWith(0);
        // On exit 0 the tool writes the PostToolUse hook document (--hook-json) and the wrapper is a
        // passthrough of it: Claude Code parses an exit-0 hook's stdout, so what the check wrote has to
        // arrive there verbatim, unprefixed and not diverted to stderr. The stub stands in for the tool,
        // so what this row measures is the passthrough rather than the document.
        run.Result.StandardOutput.NormalizedLines()
            .ShouldContain(CannedReport);
        run.Result.StandardError.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(Sh)]
    [InlineData(Pwsh)]
    public void CleanCheckThatWroteNothing_StaysSilent(string interpreter)
    {
        // The other half of that row, and the half that keeps the channel worth having: a clean check with
        // no warnings writes nothing at all, and the wrapper must not turn nothing into a blank line. A hook
        // that speaks on every edit is a hook people turn off.
        HookRun run = Fire(interpreter, payload: "", stubExit: 0, quiet: true);

        run.ShouldExitWith(0);
        run.Result.StandardOutput.ShouldBeEmpty();
        run.Result.StandardError.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(Sh)]
    [InlineData(Pwsh)]
    public void RedRule_BlocksWithExitTwoAndTheReportOnStderr(string interpreter)
    {
        HookRun run = Fire(interpreter, payload: "", stubExit: 1);

        // 2 is how a Claude Code hook blocks, and the report on stderr is what the agent reads to self-correct.
        run.ShouldExitWith(2);
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
        run.ShouldExitWith(1);
        run.Result.StandardError.NormalizedLines()
            .ShouldStartWith("loadbearing config error:");
    }

    [Theory]
    [InlineData(Sh)]
    [InlineData(Pwsh)]
    public void NonCodePayload_SkipsTheCheckEntirely(string interpreter)
    {
        HookRun run = Fire(interpreter, DocsEditPayload, stubExit: 0);

        run.ShouldExitWith(0);
        run.ShouldNotHaveRunTheCheckIn(run.ProjectDirectory,
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

        run.ShouldExitWith(0);
        run.ShouldHaveRunTheCheckIn(run.ProjectDirectory,
            "A .cs edit must reach the check, and the sentinel only lands if the wrapper both invoked "
            + "loadbearing and changed to CLAUDE_PROJECT_DIR first.");
    }

    [Theory]
    [InlineData(Sh)]
    [InlineData(Pwsh)]
    public void AbsolutePayloadInsideTheProjectDirectory_RunsTheCheck(string interpreter)
    {
        // A real repository, because this is the everyday path: both sides resolve to the same working
        // tree and the run proceeds. Without one they resolve to nothing, which proceeds by fallback and
        // would pass just as well with the comparison inverted.
        HookSandbox sandbox = ArrangeRepository();
        string edited = Path.Combine(sandbox.ProjectDirectory, "src", "Probe.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(edited)!);

        HookRun run = Fire(interpreter, sandbox, PayloadNaming(edited), stubExit: 0);

        run.ShouldExitWith(0);
        run.ShouldHaveRunTheCheckIn(run.ProjectDirectory,
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

        run.ShouldExitWith(0);
        run.ShouldNotHaveRunTheCheckIn(run.ProjectDirectory,
            "An edit outside CLAUDE_PROJECT_DIR must not buy a check of the project: the verdict would be "
            + "about code the edit never touched. This is also the leg that covers a file in no repository "
            + "at all, a scratch directory being the everyday case.");
    }

    [Theory]
    [InlineData(Sh)]
    [InlineData(Pwsh)]
    public void PayloadInALinkedWorktree_SkipsTheCheck(string interpreter)
    {
        HookSandbox sandbox = ArrangeRepository();
        string worktree = AddLinkedWorktree(sandbox);

        HookRun run = Fire(interpreter, sandbox, PayloadNaming(Path.Combine(worktree, "Probe.cs")), stubExit: 0);

        run.ShouldExitWith(0);
        run.ShouldNotHaveRunTheCheckIn(run.ProjectDirectory,
            "A session in the main checkout writing to a worktree by absolute path is inside the project "
            + "directory by path and in a different working tree by git. Only the second answer is the "
            + "right one, so the check must not run.");
    }

    [Theory]
    [InlineData(Sh)]
    [InlineData(Pwsh)]
    public void SessionInsideALinkedWorktree_SkipsTheCheck(string interpreter)
    {
        HookSandbox sandbox = ArrangeRepository();
        string worktree = AddLinkedWorktree(sandbox);

        HookRun run = Fire(interpreter, sandbox, payload: "", stubExit: 0, workingDirectory: worktree);

        run.ShouldExitWith(0);
        run.ShouldNotHaveRunTheCheckIn(run.ProjectDirectory,
            "With no payload the current directory stands in for the edited file, so a hand-run inside a "
            + "worktree opts itself out rather than checking the main checkout CLAUDE_PROJECT_DIR names.");
    }

    [Theory]
    [InlineData(Sh)]
    [InlineData(Pwsh)]
    public void SessionInASubdirectoryOfTheProject_RunsTheCheck(string interpreter)
    {
        HookSandbox sandbox = ArrangeRepository();
        string subdirectory = Path.Combine(sandbox.ProjectDirectory, "src");
        Directory.CreateDirectory(subdirectory);

        HookRun run = Fire(interpreter, sandbox, payload: "", stubExit: 0, workingDirectory: subdirectory);

        run.ShouldExitWith(0);
        run.ShouldHaveRunTheCheckIn(run.ProjectDirectory,
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

        run.ShouldExitWith(0);
        run.ShouldHaveRunTheCheckIn(run.WorkingDirectory,
            "A hand-run outside Claude Code has no CLAUDE_PROJECT_DIR to compare anything against, so the "
            + "guard steps aside and the check runs where the run was launched.");
    }

    [Theory]
    [InlineData(Sh)]
    [InlineData(Pwsh)]
    public void StopPayload_RedRule_BlocksWithExitTwoAndTheReportOnStderr(string interpreter)
    {
        HookSandbox sandbox = ArrangeRepository();
        string session = NewSession();

        HookRun run = Fire(interpreter, sandbox, StopPayload(sandbox, session), stubExit: 1);

        // Same 2 the per-edit shape blocks with, and at the turn boundary it means something stronger: the
        // agent said it was done and the hook refuses the stop, with the report as the reason to keep going.
        run.ShouldExitWith(2);
        run.Result.StandardError.NormalizedLines()
            .ShouldContain(CannedReport);
        // Both files are written before the wrapper exits, because the next stop's decisions are theirs: the
        // verdict is what a skip would have to trust, and the count is what the cap reads.
        ReadState(sandbox, session, "verdict")
            .ShouldStartWith("red");
        ReadState(sandbox, session, "rounds")
            .Trim()
            .ShouldBe("p1 1");
    }

    [Theory]
    [InlineData(Sh)]
    [InlineData(Pwsh)]
    public void StopPayload_CleanRunWithWarnings_PassesTheDocumentThrough(string interpreter)
    {
        HookSandbox sandbox = ArrangeRepository();
        string session = NewSession();

        HookRun run = Fire(interpreter, sandbox, StopPayload(sandbox, session), stubExit: 0);

        // A tripwire warning has no verdict to change and everything to say. Exit 0 with the document on
        // stdout is how Claude Code turns it into one continuation of the turn rather than a block.
        run.ShouldExitWith(0);
        run.Result.StandardOutput.NormalizedLines()
            .ShouldContain(CannedReport);
        run.Result.StandardError.ShouldBeEmpty();
        ReadState(sandbox, session, "verdict")
            .ShouldStartWith("green");
        // Counted like a block: a continuation is a continuation, and the cap exists to bound them all.
        ReadState(sandbox, session, "rounds")
            .Trim()
            .ShouldBe("p1 1");
    }

    [Theory]
    [InlineData(Sh)]
    [InlineData(Pwsh)]
    public void StopPayload_CleanSilentRun_StaysSilent(string interpreter)
    {
        HookSandbox sandbox = ArrangeRepository();
        string session = NewSession();

        HookRun run = Fire(interpreter, sandbox, StopPayload(sandbox, session), stubExit: 0, quiet: true);

        // The everyday turn: the check is clean, the hook says nothing, and the stop stands.
        run.ShouldExitWith(0);
        run.Result.StandardOutput.ShouldBeEmpty();
        run.Result.StandardError.ShouldBeEmpty();
        ReadState(sandbox, session, "verdict")
            .ShouldStartWith("green");
        File.Exists(StatePath(sandbox, session, "rounds"))
            .ShouldBeFalse(
                "A silent green ends the continuations, so the count goes rather than standing to be read "
                + "by a later prompt that has nothing to do with it.");
    }

    [Theory]
    [InlineData(Sh)]
    [InlineData(Pwsh)]
    public void StopPayload_UnchangedGreenTree_SkipsTheCheck(string interpreter)
    {
        HookSandbox sandbox = ArrangeRepository();
        WriteProbe(sandbox);
        string session = NewSession();
        string payload = StopPayload(sandbox, session);

        HookRun first = Fire(interpreter, sandbox, payload, stubExit: 0, quiet: true);
        first.ShouldHaveRunTheCheckIn(
            sandbox.ProjectDirectory, "The first stop has no verdict to trust, so it checks.");
        File.Delete(Path.Combine(sandbox.ProjectDirectory, Sentinel));

        HookRun second = Fire(interpreter, sandbox, payload, stubExit: 0, quiet: true);

        // The whole reason a question-and-answer turn is free. Nothing about the tree has moved since the
        // green verdict — same HEAD, same changed-code listing, nothing written since — so there is no
        // question left for a check to answer, and a hook that ran one anyway would cost the turn a minute.
        second.ShouldExitWith(0);
        second.Result.StandardOutput.ShouldBeEmpty();
        second.ShouldNotHaveRunTheCheckIn(
            sandbox.ProjectDirectory,
            "A second stop over an unchanged tree with a green verdict must not spend a check at all.");
    }

    [Theory]
    [InlineData(Sh)]
    [InlineData(Pwsh)]
    public void StopPayload_EditedTree_ChecksAgain(string interpreter)
    {
        HookSandbox sandbox = ArrangeRepository();
        string probe = WriteProbe(sandbox);
        string session = NewSession();
        string payload = StopPayload(sandbox, session);

        Fire(interpreter, sandbox, payload, stubExit: 0, quiet: true);
        File.Delete(Path.Combine(sandbox.ProjectDirectory, Sentinel));
        // An edit to a file already in the listing is invisible to the listing: untracked before, untracked
        // after. Its timestamp is the only thing that moved, which is why the verdict is compared against
        // one. Backdated so the two writes cannot land in the same tick of a coarse filesystem clock.
        Backdate(StatePath(sandbox, session, "verdict"));
        File.AppendAllText(probe, "\n// edited after the verdict\n");

        HookRun second = Fire(interpreter, sandbox, payload, stubExit: 0, quiet: true);

        second.ShouldHaveRunTheCheckIn(
            sandbox.ProjectDirectory,
            "A file the verdict covered was written after it, so the verdict no longer speaks for the tree "
            + "and the check has to run again.");
    }

    [Theory]
    [InlineData(Sh)]
    [InlineData(Pwsh)]
    public void StopPayload_RedVerdict_NeverSkips(string interpreter)
    {
        HookSandbox sandbox = ArrangeRepository();
        WriteProbe(sandbox);
        string session = NewSession();
        string payload = StopPayload(sandbox, session);

        Fire(interpreter, sandbox, payload, stubExit: 1);
        File.Delete(Path.Combine(sandbox.ProjectDirectory, Sentinel));

        HookRun second = Fire(interpreter, sandbox, payload, stubExit: 1);

        // The skip is an optimisation over a verdict that was clean. A red one is the state the hook exists
        // to keep saying out loud, and a tree that has not changed is precisely a tree still carrying it.
        second.ShouldExitWith(2);
        second.ShouldHaveRunTheCheckIn(
            sandbox.ProjectDirectory, "A red verdict is never skipped, however unchanged the tree is.");
    }

    [Theory]
    [InlineData(Sh)]
    [InlineData(Pwsh)]
    public void StopPayload_AtTheRoundCap_StopsBlocking(string interpreter)
    {
        HookSandbox sandbox = ArrangeRepository();
        string session = NewSession();
        string payload = StopPayload(sandbox, session);

        for (var round = 0; round < 3; round++)
            Fire(interpreter, sandbox, payload, stubExit: 1)
                .ShouldExitWith(2, $"Round {round + 1} is inside the cap and still blocks.");

        HookRun capped = Fire(interpreter, sandbox, payload, stubExit: 1);

        // A rule the agent cannot satisfy would otherwise be a loop the user watches. At the cap the hook
        // says so on stderr and exits 1, which is not a block: the user reads it and the turn ends.
        capped.ShouldExitWith(1);
        capped.Result.StandardError.NormalizedLines()
            .ShouldStartWith("loadbearing: still red after 3 rounds; not blocking again.");
        capped.Result.StandardError.NormalizedLines()
            .ShouldContain(CannedReport);

        HookRun nextPrompt = Fire(interpreter, sandbox, StopPayload(sandbox, session, promptId: "p2"), stubExit: 1);

        // The count is per prompt, so the next thing the user asks for starts with its own three rounds
        // rather than inheriting a stand-down from the work before it.
        nextPrompt.ShouldExitWith(2);
    }

    [Theory]
    [InlineData(Sh)]
    [InlineData(Pwsh)]
    public void StopPayload_InAWorktreeSession_ChecksTheWorktree(string interpreter)
    {
        HookSandbox sandbox = ArrangeRepository();
        string worktree = AddLinkedWorktree(sandbox);

        HookRun run = Fire(
            interpreter, sandbox, StopPayload(sandbox, NewSession(), cwd: worktree), stubExit: 0, quiet: true);

        // The per-edit guard skips a worktree because the edit belongs to a tree the hook does not speak
        // for. At a stop the worktree *is* the session's tree, so the answer inverts: check it, and check it
        // there. The config values are root-relative, which is what lets one wrapper serve both trees.
        run.ShouldExitWith(0);
        run.ShouldHaveRunTheCheckIn(worktree, "A session inside a linked worktree must have its own tree checked.");
        run.ShouldNotHaveRunTheCheckIn(
            sandbox.ProjectDirectory,
            "and checked there — a verdict about the main checkout would be about code this session never "
            + "touched.");
    }

    [Theory]
    [InlineData(Sh)]
    [InlineData(Pwsh)]
    public void SubagentStopPayload_BehavesAsStop(string interpreter)
    {
        HookSandbox sandbox = ArrangeRepository();
        string session = NewSession();

        HookRun run = Fire(
            interpreter, sandbox, StopPayload(sandbox, session, eventName: "SubagentStop"), stubExit: 1);

        // A worker claiming to be done is the same claim at a smaller scale, and its edits are in the tree
        // by the time it makes it. One branch serves both events rather than two that could drift.
        run.ShouldExitWith(2);
        run.Result.StandardError.NormalizedLines()
            .ShouldContain(CannedReport);
        ReadState(sandbox, session, "verdict")
            .ShouldStartWith("red");
    }

    [Theory]
    [InlineData(Sh)]
    [InlineData(Pwsh)]
    public void StopPayload_UnwritableStateDir_HonoursStopHookActive(string interpreter)
    {
        HookSandbox sandbox = ArrangeRepository();
        // A file where the state root should be: everything under it fails to open, on both interpreters.
        string blocked = Path.Combine(sandbox.Root, "state-root-is-a-file");
        File.WriteAllText(blocked, "not a directory");

        HookRun run = Fire(
            interpreter, sandbox, StopPayload(sandbox, NewSession(), stopHookActive: true), stubExit: 1,
            stateDirectory: blocked);

        // With nowhere to count rounds the wrapper degrades to the one round the payload itself carries:
        // stop_hook_active says this hook has already blocked in this turn, so it stands down rather than
        // blocking a second time on a count it cannot keep.
        run.ShouldExitWith(0);
        run.Result.StandardError.ShouldBeEmpty();
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
    private static HookRun Fire(string interpreter, string payload, int stubExit, bool quiet = false)
    {
        return Fire(interpreter, Arrange(), payload, stubExit, quiet: quiet);
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
    ///     environment. <paramref name="quiet" /> silences the stub, which is how a clean check with no
    ///     warnings — the tool's own silence under <c>--hook-json</c> — is put in front of a wrapper.
    /// </remarks>
    private static HookRun Fire(
        string interpreter,
        HookSandbox sandbox,
        string payload,
        int stubExit,
        string? workingDirectory = null,
        bool withProjectDirectory = true,
        bool quiet = false,
        string? stateDirectory = null)
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
        startInfo.Environment["LOADBEARING_HOOK_STATE_DIR"] =
            ShellInterpreter.Posix(stateDirectory ?? sandbox.StateDirectory);
        startInfo.Environment["STUB_EXIT"] = stubExit.ToString(CultureInfo.InvariantCulture);
        if (quiet) startInfo.Environment["STUB_QUIET"] = "1";
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
        string stateDirectory = Path.Combine(root, "state");
        Directory.CreateDirectory(projectDirectory);
        Directory.CreateDirectory(stubDirectory);
        Directory.CreateDirectory(stateDirectory);
        WriteStub(stubDirectory);

        return new HookSandbox(root, projectDirectory, stubDirectory, stateDirectory);
    }

    /// <summary>
    ///     The same sandbox with a git repository under its project directory: what every row that fires at
    ///     a working tree wants, and the only shape in which a linked worktree can be added or a turn-end
    ///     verdict can have a HEAD to record.
    /// </summary>
    private static HookSandbox ArrangeRepository()
    {
        HookSandbox sandbox = Arrange();
        InitialiseRepository(sandbox);

        return sandbox;
    }

    /// <summary>
    ///     Makes the sandbox's project directory a git repository with one empty commit — enough for a
    ///     working tree to exist and for a worktree to be added, and nothing more.
    /// </summary>
    /// <remarks>
    ///     Two child processes rather than four: the identity rides on the commit as <c>-c</c> overrides
    ///     beside the signing one, rather than being written into the repository by a pair of
    ///     <c>git config</c> runs first. Making this one commit succeed whatever the host's global git
    ///     config says is all it is ever for — nothing here commits again, and every git command the
    ///     wrappers themselves run is a read. Every row in the file pays this twice over, once per
    ///     interpreter.
    /// </remarks>
    private static void InitialiseRepository(HookSandbox sandbox)
    {
        GitCommand.Run(sandbox.ProjectDirectory, "init");
        GitCommand.Run(
            sandbox.ProjectDirectory,
            "-c", "user.email=loadbearing-test@example.invalid",
            "-c", "user.name=LoadBearing Test",
            "-c", "commit.gpgsign=false",
            "commit", "--allow-empty", "-m", "baseline");
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

    /// <summary>
    ///     A turn-end payload: the five fields the wrapper reads out of a <c>Stop</c> or
    ///     <c>SubagentStop</c> document, with <paramref name="cwd" /> defaulting to the sandbox's project
    ///     directory — the session's own tree, which is what the branch resolves the check's tree from.
    /// </summary>
    private static string StopPayload(
        HookSandbox sandbox,
        string sessionId,
        string promptId = "p1",
        bool stopHookActive = false,
        string eventName = "Stop",
        string? cwd = null)
    {
        string escaped = (cwd ?? sandbox.ProjectDirectory).Replace("\\", "\\\\");
        string active = stopHookActive ? "true" : "false";

        // Three dollars, because the payload's own closing braces are the last thing in the literal.
        return $$$"""
                  {"hook_event_name":"{{{eventName}}}","session_id":"{{{sessionId}}}","prompt_id":"{{{promptId}}}","stop_hook_active":{{{active}}},"cwd":"{{{escaped}}}"}
                  """;
    }

    /// <summary>
    ///     A session id of this row's own, so rows sharing the machine's clock cannot read each other's
    ///     verdict — the state directory is keyed by session, as a live session's is.
    /// </summary>
    private static string NewSession()
    {
        return Guid.NewGuid()
            .ToString("N");
    }

    private static string StatePath(HookSandbox sandbox, string sessionId, string name)
    {
        return Path.Combine(sandbox.StateDirectory, "loadbearing-hook", sessionId, name);
    }

    private static string ReadState(HookSandbox sandbox, string sessionId, string name)
    {
        string path = StatePath(sandbox, sessionId, name);
        File.Exists(path)
            .ShouldBeTrue($"The wrapper should have written its {name} file at {path}.");

        return File.ReadAllText(path);
    }

    /// <summary>
    ///     An untracked <c>src/Probe.cs</c> in the sandbox: one code file for the changed-code listing to
    ///     carry, and one timestamp for the skip to compare against.
    /// </summary>
    private static string WriteProbe(HookSandbox sandbox)
    {
        string probe = Path.Combine(sandbox.ProjectDirectory, "src", "Probe.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(probe)!);
        File.WriteAllText(probe, "public sealed class Probe;\n");

        return probe;
    }

    /// <summary>
    ///     Pushes <paramref name="path" />'s write time ten seconds into the past, so a write made straight
    ///     afterwards is unambiguously newer than it whatever the filesystem's timestamp granularity.
    /// </summary>
    private static void Backdate(string path)
    {
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path)
            .AddSeconds(-10));
    }

    /// <summary>
    ///     Writes the stub <c>loadbearing</c> the wrappers resolve off <c>PATH</c>: it records that it ran,
    ///     prints the canned two-line report unless <c>STUB_QUIET</c> is set, and exits <c>STUB_EXIT</c>.
    /// </summary>
    /// <remarks>
    ///     The quiet knob is the real tool's exit-0-with-nothing-to-say: a clean check with no warnings
    ///     writes nothing under <c>--hook-json</c>, and the wrapper has to pass that nothing through as
    ///     nothing. Without a stub that can be silent, the row could only be written by asserting on the
    ///     absence of a string the stub had printed anyway.
    /// </remarks>
    private static void WriteStub(string stubDirectory)
    {
        ShellInterpreter.WriteExecutableStub(
            stubDirectory,
            "loadbearing",
            "#!/bin/sh\n"
            + $"printf 'ran\\n' > {Sentinel}\n"
            + "if [ -z \"${STUB_QUIET:-}\" ]; then\n"
            + "  printf 'FAIL arch/stub-rule -- canned violation report\\n'\n"
            + "  printf '  Probe.cs:10 -- second report line, so the multi-line path is covered\\n'\n"
            + "fi\n"
            + "exit \"${STUB_EXIT:-0}\"\n",
            // echo( is the batch form that echoes leading whitespace verbatim, which the report's second line
            // needs; goto is the batch form of a multi-statement conditional.
            "@echo off\r\n"
            + $"echo ran>{Sentinel}\r\n"
            + "if not \"%STUB_QUIET%\"==\"\" goto :quiet\r\n"
            + "echo(FAIL arch/stub-rule -- canned violation report\r\n"
            + "echo(  Probe.cs:10 -- second report line, so the multi-line path is covered\r\n"
            + ":quiet\r\n"
            + "exit /b %STUB_EXIT%\r\n");
    }

    private readonly record struct HookSandbox(
        string Root,
        string ProjectDirectory,
        string StubDirectory,
        string StateDirectory);
}

/// <summary>
///     One wrapper run: what the child process did, the project directory it was anchored to, and the
///     directory it was launched from — the three things an assertion about a run has to be able to name.
/// </summary>
internal readonly record struct HookRun(
    ChildProcess.ProcessResult Result,
    string ProjectDirectory,
    string WorkingDirectory);
