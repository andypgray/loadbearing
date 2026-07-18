using Zphil.LoadBearing.Rendering;

namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     The two file-system probes the warm session and both persisted caches share to decide, with zero
///     MSBuild, whether a project's on-disk shape has moved: the <see cref="Enumerate">cone</see> of
///     <c>*.cs</c> a project directory globs, and the <see cref="Ancestors">ancestor chain</see> MSBuild's
///     directory-scoped imports (<c>Directory.Build.props</c>/<c>.targets</c>, <c>global.json</c>) walk.
/// </summary>
/// <remarks>
///     Hoisted so the three consumers — <see cref="WorkspaceSession" />, <see cref="ExtractionCacheStore" />,
///     and <see cref="Replay.BinlogCaptureStore" /> — cannot drift apart on what counts as a source file or
///     how far the probe chain reaches. Every yielded path is a canonical full path, so a caller keys the
///     results against a set built with <see cref="PathComparison.Comparer" /> and compares like for like.
/// </remarks>
internal static class ProjectCone
{
    /// <summary>
    ///     Enumerates every <c>*.cs</c> under <paramref name="projectDirectory" /> (recursively), skipping the
    ///     <c>bin</c>/<c>obj</c> build-output subtrees, as canonical full paths. This is the SDK default-glob
    ///     cone — the source-membership set a stat sweep cannot see change, because an SDK-glob add touches no
    ///     MSBuild file. A non-existent directory yields nothing. The cone is deliberately a superset of a
    ///     project's <em>compiled</em> documents: a <c>&lt;Compile Remove&gt;</c>'d or <c>None</c>-typed
    ///     <c>*.cs</c> still lives here, so callers must record cone membership to tell a genuine add from a
    ///     file that was never compiled in the first place.
    /// </summary>
    public static IEnumerable<string> Enumerate(string projectDirectory)
    {
        if (!Directory.Exists(projectDirectory)) yield break;

        foreach (string file in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories))
            if (!IsBuildArtifact(projectDirectory, file))
                yield return Path.GetFullPath(file);
    }

    /// <summary>
    ///     Yields <paramref name="startDirectory" /> and each ancestor directory up to the filesystem root.
    ///     The probe chain walks the whole way up rather than stopping at the solution directory: MSBuild
    ///     honors a <c>Directory.Build.props</c>/<c>.targets</c> or <c>global.json</c> found <em>above</em> the
    ///     solution too, so a stamp chain that stopped at the solution directory would miss one and let it flip
    ///     silently. The extra levels are absent-file stamps, which are cheap.
    /// </summary>
    public static IEnumerable<string> Ancestors(string startDirectory)
    {
        string? directory = startDirectory;
        while (directory is not null)
        {
            yield return directory;
            directory = Path.GetDirectoryName(directory);
        }
    }

    private static bool IsBuildArtifact(string projectDirectory, string file)
    {
        string relative = Path.GetRelativePath(projectDirectory, file).Replace('\\', '/');
        return relative.Split('/').Any(segment => segment is "bin" or "obj");
    }
}