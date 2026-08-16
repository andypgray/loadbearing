using System.Runtime.CompilerServices;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     A <see cref="TempFixtureWorkspace" /> that is also an initialized git repository with the fixture
///     committed at HEAD — the substrate for a <c>check --diff-base</c> run.
/// </summary>
/// <remarks>
///     A <c>.gitignore</c> excluding <c>bin/</c> and <c>obj/</c> is written <em>before</em> <c>git init</c>
///     so restored build artifacts never enter the index; identity is set locally and commit signing is
///     disabled, so the commit succeeds regardless of the host's global git config. Rooted in
///     <c>%TEMP%</c>, outside this repository, so <c>git init</c> is safe. The lease underneath is the
///     workspace's own — one leased tree per consuming test class, reset to pristine between facts, with
///     the previous fact's <c>.git</c> pruned by the reset and a fresh one initialized here.
/// </remarks>
internal sealed class TempGitRepo : IDisposable
{
    private readonly TempFixtureWorkspace _workspace;

    /// <param name="callerFilePath">
    ///     Compiler-supplied; never passed explicitly. Threaded through to <see cref="TempFixtureWorkspace" />
    ///     so the lease is keyed on the <em>consumer's</em> source file rather than on this one. Six
    ///     consumers across four classes used to collide on the single key <c>TempGitRepo.cs</c> minted, and
    ///     since a lease is exclusive, five of the six silently fell through to a private copy — each paying
    ///     a fresh <c>dotnet restore</c> and a permanently cold extraction cache.
    /// </param>
    public TempGitRepo([CallerFilePath] string callerFilePath = "")
    {
        _workspace = new TempFixtureWorkspace(callerFilePath: callerFilePath);
        File.WriteAllText(Path.Combine(Root, ".gitignore"), "bin/\nobj/\n");
        GitCommand.Run(Root, "init");
        GitCommand.Run(Root, "config", "user.email", "loadbearing-test@example.invalid");
        GitCommand.Run(Root, "config", "user.name", "LoadBearing Test");
        GitCommand.Run(Root, "add", "-A");
        GitCommand.Run(Root, "-c", "commit.gpgsign=false", "commit", "-m", "fixture baseline");
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
}
