using System.ComponentModel;
using System.Diagnostics;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli.Diff;

/// <summary>
///     Resolves the files changed since a git ref into a <see cref="DiffContext" /> for the Freeze
///     tripwire (GRAMMAR §7). Runs three git commands rooted at the solution directory (<c>git -C</c>):
///     <c>rev-parse --show-toplevel</c> (the repo root the paths are relative to),
///     <c>diff --name-only -z &lt;ref&gt; --</c> (tracked changes — committed since the ref, staged, and
///     unstaged worktree), and <c>ls-files --others --exclude-standard --full-name -z</c> (untracked
///     files — the agent-hook case, an agent writing a brand-new file into dragon territory). Both
///     path-listing commands are forced toplevel-relative (<c>diff</c> is by default; <c>ls-files</c>
///     needs <c>--full-name</c>), then rebased onto the toplevel. Every failure is loud: git missing on
///     PATH, not a repository, a bad ref, or a timeout all throw <see cref="UserErrorException" />
///     (exit 2). The parse/compose halves are pure and unit-pinned.
/// </summary>
internal static class GitChangedFiles
{
    private const int TimeoutMs = 30_000;

    /// <summary>
    ///     Runs git and returns the union of tracked-since-<paramref name="baseRef" /> and untracked
    ///     files as a <see cref="DiffContext" /> (absolute, forward-slash paths).
    /// </summary>
    public static DiffContext Resolve(string baseRef, string solutionDirectory)
    {
        string toplevel = Path.GetFullPath(RunGit(solutionDirectory, "rev-parse", "--show-toplevel").Trim());

        var tracked = ParseZTerminated(RunGit(solutionDirectory, "diff", "--name-only", "-z", baseRef, "--"));
        var untracked = ParseZTerminated(RunGit(solutionDirectory, "ls-files", "--others", "--exclude-standard", "--full-name", "-z"));

        var files = ComposeAbsolute(toplevel, tracked.Concat(untracked));
        return new DiffContext(baseRef, solutionDirectory, files);
    }

    /// <summary>Splits git's NUL-terminated output into non-empty entries (tolerates a missing trailing NUL).</summary>
    internal static IReadOnlyList<string> ParseZTerminated(string output)
    {
        return output.Split('\0').Where(entry => entry.Length > 0).ToList();
    }

    /// <summary>
    ///     Rebases toplevel-relative paths onto absolute, forward-slash paths, deduped per-OS
    ///     (<see cref="PathComparison" />) in first-seen order — so two case-variant changed files stay
    ///     distinct on a case-sensitive file system rather than one silently swallowing the other.
    /// </summary>
    internal static IReadOnlyList<string> ComposeAbsolute(string toplevel, IEnumerable<string> relative)
    {
        var seen = new HashSet<string>(PathComparison.Comparer);
        var result = new List<string>();
        foreach (string rel in relative)
        {
            string absolute = Path.GetFullPath(Path.Combine(toplevel, rel)).Replace('\\', '/');
            if (seen.Add(absolute)) result.Add(absolute);
        }

        return result;
    }

    private static string RunGit(string solutionDirectory, params string[] arguments)
    {
        var psi = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = solutionDirectory
        };
        // -C roots git at the solution directory regardless of the host process's cwd.
        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(solutionDirectory);
        foreach (string argument in arguments) psi.ArgumentList.Add(argument);

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw Failure("git could not be started.");
        }
        catch (Win32Exception ex)
        {
            // git is not on PATH (or is not executable).
            throw Failure($"git could not be run ({ex.Message}); --diff-base needs git on PATH.");
        }

        using (process)
        {
            using var timeout = new CancellationTokenSource(TimeoutMs);
            // Drain both streams concurrently AND cancellably. The old code read stdout with a synchronous,
            // unbounded ReadToEnd() *before* the WaitForExit timeout — so a git child whose stdout write
            // handle was inherited by a concurrently-spawned long-lived process never reached EOF and wedged
            // here forever, with the timeout below unreachable. The token now bounds every wait.
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
                string output = outputTask.GetAwaiter().GetResult();

                if (process.ExitCode != 0)
                {
                    string err = stderrTask.GetAwaiter().GetResult().Trim();
                    string suffix = err.Length > 0 ? $": {err}" : ".";
                    throw Failure($"'git {string.Join(" ", arguments)}' exited with code {process.ExitCode}{suffix}");
                }

                return output;
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                TryKill(process);
                throw Failure($"'git {string.Join(" ", arguments)}' did not complete within {TimeoutMs / 1000} seconds.");
            }
        }
    }

    // Best-effort kill of a wedged git process (and any children) before we throw the timeout.
    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(true);
        }
        catch
        {
            // Already exited or inaccessible — nothing to clean up.
        }
    }

    private static UserErrorException Failure(string detail)
    {
        return new UserErrorException($"--diff-base could not resolve changed files: {detail}");
    }
}