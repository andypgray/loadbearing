namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     A private, restored copy of the MyApp fixture solution in a unique temp directory — the substrate
///     for the Phase 5 mutation tests (<c>baseline --init</c>/<c>--accept-reductions</c>, tamper, file
///     move). The shared <see cref="WorkspaceFixture" /> output tree is read-only and shared across the
///     assembly, so a test that edits fixture files or baselines must run against its own copy. Build
///     artifacts (<c>bin</c>/<c>obj</c>) are skipped and the copy is re-restored, because a copied
///     <c>project.assets.json</c> carries absolute paths back to the original. Each instance costs a
///     restore, so batch a test's assertions over as few CLI runs as possible.
/// </summary>
internal sealed class TempFixtureWorkspace : IDisposable
{
    private readonly string _root;

    public TempFixtureWorkspace()
    {
        string source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TestSolutions", "MyApp");
        _root = Path.Combine(Path.GetTempPath(), "loadbearing-mutation", Guid.NewGuid().ToString("N"));
        CopyTree(source, _root);
        FixtureRestorer.Restore(SolutionPath);
    }

    /// <summary>Absolute path to the copied solution file.</summary>
    public string SolutionPath => Path.Combine(_root, "MyApp.sln");

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
}