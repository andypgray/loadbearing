namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     Copies a built spec's output directory to a run-scoped temp directory with named files withheld,
///     and returns the staged spec DLL's path.
/// </summary>
/// <remarks>
///     <para>
///         This is how the spec-load failure arms get driven end to end without a fixture project that
///         exists only to be corrupt. A spec whose dependency is not beside it is an ordinary deployment
///         mistake — a class-library build that never staged a package assembly, a copy that missed a
///         file — and the honest way to reproduce it is to stage a real build and leave the dependency
///         out, rather than to break it at build time and then have to keep it broken.
///     </para>
///     <para>
///         Withholding is by file name, matched at any depth, so the caller names the DLL rather than a
///         path. The staged copy is a directory of its own: <c>SpecLoadContext</c> resolves through an
///         <c>AssemblyDependencyResolver</c> rooted at the spec DLL, which for a net48 build (no
///         <c>.deps.json</c>) falls back to the spec's own directory — so what is absent from the staged
///         directory is genuinely absent from resolution.
///     </para>
///     <para>
///         <b>Staging intact and then deleting is a different case, not a longer way to withhold.</b> A net10
///         spec carries a <c>.deps.json</c>, and the resolver reads it once when the load context is
///         constructed while re-checking the disk on every resolve. Withholding therefore means "never
///         staged", which the resolver sees from the first call; deleting from the staged copy afterwards
///         means "was there, and a rebuild took it away underneath a running host", which it sees only on the
///         next one. <c>SpecLoadContextFallbackTests</c> needs the second and stages intact to get it.
///     </para>
/// </remarks>
internal static class SpecOutputStager
{
    /// <summary>
    ///     Stages the directory containing <paramref name="specDllPath" />, omitting every file whose name
    ///     matches one of <paramref name="withheldFileNames" /> (ordinal, case-insensitive), and returns the
    ///     path of the spec DLL inside the staged copy. Re-staging the same combination replaces it, so a
    ///     test never inherits a previous run's directory.
    /// </summary>
    internal static string StageWithout(string specDllPath, params string[] withheldFileNames)
    {
        string sourceDirectory = Path.GetDirectoryName(specDllPath)!;
        string specFileName = Path.GetFileName(specDllPath);
        var withheld = new HashSet<string>(withheldFileNames, StringComparer.OrdinalIgnoreCase);

        string label = Path.GetFileNameWithoutExtension(specFileName)
                       + "-without-"
                       + string.Join("-", withheldFileNames.Select(Path.GetFileNameWithoutExtension));
        string stagedDirectory = Path.Combine(TestTempRoot.For("staged-specs"), label);

        if (Directory.Exists(stagedDirectory)) Directory.Delete(stagedDirectory, true);
        CopyExcept(sourceDirectory, stagedDirectory, withheld);

        return Path.Combine(stagedDirectory, specFileName);
    }

    private static void CopyExcept(string sourceDirectory, string targetDirectory, HashSet<string> withheld)
    {
        Directory.CreateDirectory(targetDirectory);

        foreach (string file in Directory.EnumerateFiles(sourceDirectory))
        {
            string name = Path.GetFileName(file);
            if (withheld.Contains(name)) continue;
            File.Copy(file, Path.Combine(targetDirectory, name));
        }

        foreach (string directory in Directory.EnumerateDirectories(sourceDirectory))
            CopyExcept(directory, Path.Combine(targetDirectory, Path.GetFileName(directory)), withheld);
    }
}