using System.ComponentModel;
using System.Diagnostics;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli.Diff;

/// <summary>
///     Resolves the files changed since a git ref into a <see cref="DiffContext" /> for the Quarantine
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
    private const int TimeoutSeconds = 30;

    /// <summary>
    ///     Runs git and returns the union of tracked-since-<paramref name="baseRef" /> and untracked
    ///     files as a <see cref="DiffContext" /> (absolute, forward-slash paths).
    /// </summary>
    public static async Task<DiffContext> ResolveAsync(string baseRef, string solutionDirectory, CancellationToken ct)
    {
        string toplevelOutput = await RunGitAsync(solutionDirectory, ct, "rev-parse", "--show-toplevel");
        string toplevel = Path.GetFullPath(toplevelOutput.Trim());

        string trackedOutput = await RunGitAsync(solutionDirectory, ct, "diff", "--name-only", "-z", baseRef, "--");
        string untrackedOutput = await RunGitAsync(
            solutionDirectory, ct, "ls-files", "--others", "--exclude-standard", "--full-name", "-z");

        var tracked = ParseZTerminated(trackedOutput);
        var untracked = ParseZTerminated(untrackedOutput);

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

    // Every launch goes through ChildProcess, whose closed stdin is what keeps git from wedging: without
    // it the child inherits this process's stdin, which inside the MCP server is the client's live
    // JSON-RPC pipe parked on a synchronous read — and Git for Windows' startup handle probe blocks
    // forever against that. The 30s ceiling is a safety net behind it, linked with the caller's token so
    // a cancelled tool call aborts the wait instead of leaving a zombie to run the clock out.
    private static async Task<string> RunGitAsync(
        string solutionDirectory, CancellationToken ct, params string[] arguments)
    {
        var psi = new ProcessStartInfo("git")
        {
            CreateNoWindow = true,
            WorkingDirectory = solutionDirectory
        };
        // -C roots git at the solution directory regardless of the host process's cwd.
        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(solutionDirectory);
        foreach (string argument in arguments) psi.ArgumentList.Add(argument);

        ChildProcess.ProcessResult result;
        try
        {
            result = await ChildProcess.RunAsync(psi, TimeSpan.FromSeconds(TimeoutSeconds), ct: ct);
        }
        catch (Win32Exception ex)
        {
            // git is not on PATH (or is not executable).
            throw Failure($"git could not be run ({ex.Message}); --diff-base needs git on PATH.");
        }
        catch (InvalidOperationException)
        {
            throw Failure("git could not be started.");
        }
        catch (TimeoutException)
        {
            throw Failure($"'git {string.Join(" ", arguments)}' did not complete within {TimeoutSeconds} seconds.");
        }

        if (result.ExitCode != 0)
        {
            string err = result.StandardError.Trim();
            string suffix = err.Length > 0 ? $": {err}" : ".";
            throw Failure($"'git {string.Join(" ", arguments)}' exited with code {result.ExitCode}{suffix}");
        }

        return result.StandardOutput;
    }

    private static UserErrorException Failure(string detail)
    {
        return new UserErrorException($"--diff-base could not resolve changed files: {detail}");
    }
}
