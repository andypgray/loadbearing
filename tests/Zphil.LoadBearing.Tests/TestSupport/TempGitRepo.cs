using System.Diagnostics;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     A <see cref="TempFixtureWorkspace" /> that is also an initialized git repository with the fixture
///     committed at HEAD — the substrate for the Freeze tripwire git e2e (<c>check --diff-base</c>). A
///     <c>.gitignore</c> excluding <c>bin/</c> and <c>obj/</c> is written <em>before</em> <c>git init</c>
///     so restored build artifacts never enter the index; identity is set locally and commit signing is
///     disabled, so the commit succeeds regardless of the host's global git config. Rooted in <c>%TEMP%</c>
///     (outside this repo), so <c>git init</c> is safe. Each instance costs a restore plus a commit.
/// </summary>
internal sealed class TempGitRepo : IDisposable
{
    private readonly TempFixtureWorkspace _workspace = new();

    public TempGitRepo()
    {
        File.WriteAllText(Path.Combine(Root, ".gitignore"), "bin/\nobj/\n");
        Git("init");
        Git("config", "user.email", "loadbearing-test@example.invalid");
        Git("config", "user.name", "LoadBearing Test");
        Git("add", "-A");
        Git("-c", "commit.gpgsign=false", "commit", "-m", "fixture baseline");
    }

    /// <summary>Absolute path to the committed solution file.</summary>
    public string SolutionPath => _workspace.SolutionPath;

    /// <summary>The repository root (the solution directory).</summary>
    public string Root => Path.GetDirectoryName(_workspace.SolutionPath)!;

    public void Dispose()
    {
        _workspace.Dispose();
    }

    /// <summary>Absolute path to a file or directory inside the repo, from solution-relative segments.</summary>
    public string PathOf(params string[] relativeSegments)
    {
        return _workspace.PathOf(relativeSegments);
    }

    // Runs `git -C <root> <args...>`; throws on non-zero exit. Streams drain through ProcessRunner so a
    // git child whose pipe handle is inherited by a concurrently-spawned BuildHost cannot wedge the run.
    private void Git(params string[] args)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = Root,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(Root);
        foreach (string argument in args) startInfo.ArgumentList.Add(argument);

        ProcessRunner.ProcessResult result = ProcessRunner.Run(startInfo);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"'git {string.Join(" ", args)}' failed with exit code {result.ExitCode}."
                + $"{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}{result.StandardError}");
    }
}