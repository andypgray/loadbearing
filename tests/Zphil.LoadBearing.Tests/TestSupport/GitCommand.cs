using System.Diagnostics;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     Runs <c>git</c> against a directory for the suites that need a real repository underneath them —
///     a committed fixture to diff against, or a linked worktree to be fired at.
/// </summary>
/// <remarks>
///     Launched through <see cref="ChildProcess" />, so this git gets the closed stdin, the bounded wait
///     and the kill-tree every child in this repository gets. A non-zero exit throws with git's own two
///     output channels in the message, because the first question a failed setup raises is what git said.
/// </remarks>
internal static class GitCommand
{
    /// <summary>
    ///     Runs <c>git -C <paramref name="workingDirectory" /> <paramref name="args" /></c>, throwing on a
    ///     non-zero exit.
    /// </summary>
    internal static void Run(string workingDirectory, params string[] args)
    {
        Execute(workingDirectory, args);
    }

    /// <summary>
    ///     The same, returning git's standard output — line-oriented and LF-normalized, as
    ///     <see cref="ChildProcess.Run" /> captures it.
    /// </summary>
    internal static string Output(string workingDirectory, params string[] args)
    {
        return Execute(workingDirectory, args)
            .StandardOutput;
    }

    /// <summary>
    ///     <c>git init</c> in <paramref name="directory" /> plus the local identity a commit there needs, so
    ///     a commit succeeds whatever the host's global git config says — or does not say.
    /// </summary>
    /// <remarks>
    ///     Signing stays a per-commit <c>-c commit.gpgsign=false</c> at the callers rather than a repository
    ///     setting here: it is a property of the commit being made, and the callers do not all make one.
    /// </remarks>
    internal static void InitRepository(string directory)
    {
        Run(directory, "init");
        Run(directory, "config", "user.email", "loadbearing-test@example.invalid");
        Run(directory, "config", "user.name", "LoadBearing Test");
    }

    private static ChildProcess.ProcessResult Execute(string workingDirectory, string[] args)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(workingDirectory);
        foreach (string argument in args) startInfo.ArgumentList.Add(argument);

        ChildProcess.ProcessResult result = ChildProcess.Run(startInfo);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"'git {string.Join(" ", args)}' failed with exit code {result.ExitCode}."
                + $"{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}{result.StandardError}");

        return result;
    }
}
