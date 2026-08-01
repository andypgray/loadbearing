using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     A private copy of a fixture solution in a unique temp directory — the substrate for the mutation
///     tests (<c>baseline --init</c>/<c>--accept-reductions</c>, tamper, file move) and for any test that
///     needs to build a fixture rather than only read it. The shared <see cref="WorkspaceFixture" /> output
///     tree is read-only and shared across the assembly, so a test that edits fixture files or baselines
///     must run against its own copy. Build artifacts (<c>bin</c>/<c>obj</c>) are skipped and the copy is
///     re-restored, because a copied <c>project.assets.json</c> carries absolute paths back to the
///     original. Each restored instance costs a restore, so batch a test's assertions over as few CLI runs
///     as possible.
/// </summary>
/// <remarks>
///     Defaults to the MyApp fixture, which every original caller wants. The parameters exist for the
///     non-SDK-style ClassicApp fixture under <c>Fixtures/LegacySolutions/</c>, which needs the same copy
///     tree with <c>restore</c> off: a project with no <c>PackageReference</c> and no
///     <c>project.assets.json</c> has nothing to restore, so the call would only cost a process.
/// </remarks>
internal sealed class TempFixtureWorkspace : IDisposable
{
    private const string DefaultFixtureDirectory = "TestSolutions/MyApp";

    private const string DefaultSolutionFileName = "MyApp.sln";

    // The temp base, resolved through any symlinked ancestor once per run, so every downstream path —
    // copy root, solution dir, git toplevel — shares one spelling. See RealTempRoot for why the harness
    // still needs this even though production now canonicalizes at the discovery seam.
    private static readonly string TempRoot = RealTempRoot();

    private readonly string _root;

    /// <param name="fixtureDirectory">
    ///     The fixture's directory, '/'-separated and relative to the test output's <c>Fixtures/</c> root.
    /// </param>
    /// <param name="solutionFileName">The solution file's name inside that directory.</param>
    /// <param name="restore">Whether to <c>dotnet restore</c> the copy before handing it back.</param>
    public TempFixtureWorkspace(
        string fixtureDirectory = DefaultFixtureDirectory,
        string solutionFileName = DefaultSolutionFileName,
        bool restore = true)
    {
        var segments = new List<string> { AppContext.BaseDirectory, "Fixtures" };
        segments.AddRange(fixtureDirectory.Split('/'));
        string source = Path.Combine(segments.ToArray());

        _root = Path.Combine(TempRoot, "loadbearing-mutation", Guid.NewGuid().ToString("N"));
        SolutionPath = Path.Combine(_root, solutionFileName);
        CopyTree(source, _root);
        if (restore) FixtureRestorer.Restore(SolutionPath);
    }

    /// <summary>Absolute path to the copied solution file.</summary>
    public string SolutionPath { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
        catch
        {
            // best-effort: a temp tree with a locked MSBuild handle can survive to OS cleanup.
        }
    }

    /// <summary>Absolute path to a file or directory inside the copy, from solution-relative segments.</summary>
    public string PathOf(params string[] relativeSegments)
    {
        var segments = new List<string> { _root };
        segments.AddRange(relativeSegments);
        return Path.Combine(segments.ToArray());
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if (IsBuildArtifact(file, source)) continue;

            string target = destination + file.Substring(source.Length);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static bool IsBuildArtifact(string path, string source)
    {
        string relative = path.Substring(source.Length).Replace('\\', '/');
        return relative.Contains("/bin/") || relative.Contains("/obj/");
    }

    // Resolves Path.GetTempPath() through any symlinked ancestor so the returned base is canonical —
    // the one tested realpath (PathCanonicalizer) shared with production. Still needed here (not just in
    // the model) because the dotnet restore collision is independent of the model: on macOS
    // Path.GetTempPath() sits under /var (a symlink to /private/var), and a fixture rooted at the /var
    // spelling makes dotnet restore visit each project twice and collide on obj/*.nuget.* ("already
    // exists"). On Windows and Linux no ancestor is a link, so this is Path.GetTempPath() unchanged.
    private static string RealTempRoot()
    {
        return PathCanonicalizer.Resolve(Path.TrimEndingDirectorySeparator(Path.GetTempPath()));
    }
}