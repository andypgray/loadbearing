using Microsoft.Extensions.FileSystemGlobbing;
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
        if (!Directory.Exists(projectDirectory)) return [];

        // Ordinal, not the Matcher's default OrdinalIgnoreCase, so the exclude globs match build-output
        // directories exactly as BuildOutputDirectories.IsUnderBuildOutput does — the two probes must not
        // disagree about what counts as build output.
        var matcher = new Matcher(StringComparison.Ordinal);
        matcher.AddInclude("**/*.cs");
        foreach (string glob in BuildOutputDirectories.ExcludeGlobs) matcher.AddExclude(glob);
        return matcher.GetResultsInFullPath(projectDirectory);
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

    /// <summary>
    ///     Every non-source file whose state changes what a build of the project in
    ///     <paramref name="projectDirectory" /> produces: every place the layout in force could put its
    ///     <see cref="IntermediateOutputTree.AssetsPathsOf">restore assets file</see>, then each
    ///     <see cref="Ancestors">ancestor</see> crossed with
    ///     <see cref="FileStamping.StructuralProbeFileNames">every probe name</see> — absent ones included,
    ///     because a probe that later appears is itself the change.
    /// </summary>
    /// <remarks>
    ///     The composition, not just its primitives, is what the consumers must agree on: they are
    ///     deciding whether a cached model is still valid, and a probe one of them stamps and another does not
    ///     is a stale answer served confidently, with nothing red. The order is the order stamps are recorded
    ///     in, so a persisted stamp list keeps its shape.
    /// </remarks>
    /// <param name="projectDirectory">The directory holding the project file.</param>
    /// <param name="evaluatedOutputPath">
    ///     The project's evaluated output path, or null when the caller has none. Together with
    ///     <paramref name="intermediateAssemblyPath" /> it is what lets the assets file be found under a
    ///     non-default output layout; with either absent the default location is all that is stamped.
    /// </param>
    /// <param name="intermediateAssemblyPath">The project's intermediate assembly path, or null when unknown.</param>
    public static IEnumerable<string> StructuralPaths(
        string projectDirectory, string? evaluatedOutputPath, string? intermediateAssemblyPath)
    {
        foreach (string assets in
                 IntermediateOutputTree.AssetsPathsOf(projectDirectory, evaluatedOutputPath, intermediateAssemblyPath))
            yield return assets;

        foreach (string ancestor in Ancestors(projectDirectory))
        foreach (string probe in FileStamping.StructuralProbeFileNames)
            yield return Path.Combine(ancestor, probe);
    }

    /// <summary>
    ///     The cone's <c>*.cs</c> that <paramref name="known" /> does not already hold, ordinal-sorted — the
    ///     SDK-glob adds a stat sweep cannot see. Sorted so a caller keying on the result gets the same answer
    ///     whatever order the file system enumerated.
    /// </summary>
    public static IReadOnlyList<string> Adds(string projectDirectory, ICollection<string> known)
    {
        var adds = Enumerate(projectDirectory).Where(full => !known.Contains(full)).ToList();
        adds.Sort(StringComparer.Ordinal);
        return adds;
    }
}
