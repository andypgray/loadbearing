using System.IO.Enumeration;
using Zphil.LoadBearing.Rendering;

namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     The file-system probes the warm session and both persisted caches share to decide, with zero MSBuild,
///     whether a tree's on-disk shape has moved: the <see cref="Enumerate">cone</see> of <c>*.cs</c> a project
///     directory globs, and the <see cref="SolutionStructuralPaths">structural path set</see> a whole solution
///     stamps.
/// </summary>
/// <remarks>
///     Hoisted so the three consumers — <see cref="WorkspaceSession" />, <see cref="ExtractionCacheStore" />,
///     and <see cref="Replay.BinlogCaptureStore" /> — cannot drift apart on what counts as a source file or
///     how far the probe chain reaches. Every yielded path is a canonical full path, so a caller keys the
///     results against a set built with <see cref="PathComparison.Comparer" /> and compares like for like.
/// </remarks>
internal static class ProjectCone
{
    // Compatible-mode enumeration, matched deliberately to the DirectoryInfo walk this scan is defined
    // against: hidden and system entries are kept (the framework's own default skips both) and an
    // inaccessible directory throws rather than quietly yielding nothing. Either default would shrink the
    // cone silently, and a file the cone never sees is an add that is never detected — a stale model served
    // confidently, which is the one failure these probes exist to prevent.
    private static readonly EnumerationOptions ConeEnumeration = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.None,
        IgnoreInaccessible = false,
        ReturnSpecialDirectories = false
    };

    /// <summary>
    ///     Enumerates every <c>*.cs</c> under <paramref name="projectDirectory" /> (recursively), skipping the
    ///     <c>bin</c>/<c>obj</c> build-output subtrees, as canonical full paths. This is the SDK default-glob
    ///     cone — the source-membership set a stat sweep cannot see change, because an SDK-glob add touches no
    ///     MSBuild file. A non-existent directory yields nothing. The cone is deliberately a superset of a
    ///     project's <em>compiled</em> documents: a <c>&lt;Compile Remove&gt;</c>'d or <c>None</c>-typed
    ///     <c>*.cs</c> still lives here, so callers must record cone membership to tell a genuine add from a
    ///     file that was never compiled in the first place.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Lazy, and that is the point.</b> Two of the three callers ask only whether the walk finds
    ///         <em>anything</em> new and stop at the first hit — the warm session's per-call reconcile sweep
    ///         among them, which runs on every MCP tool call. A scan that materialized the cone first paid
    ///         for the whole tree before either could exit, so the early exits were decoration. Streaming the
    ///         walk is what makes them real; the callers that do need the whole set are unaffected.
    ///     </para>
    ///     <para>
    ///         Both tests are ordinal — the <c>.cs</c> suffix and the pruned directory names — so a
    ///         differently-cased <c>BIN</c> is not build output and a <c>.CS</c> is not source, matching
    ///         <see cref="BuildOutputDirectories.IsUnderBuildOutput" /> exactly. Order is whatever the file
    ///         system yields: <see cref="Adds" /> sorts, and no caller may key on the raw order.
    ///     </para>
    /// </remarks>
    public static IEnumerable<string> Enumerate(string projectDirectory)
    {
        string root = Path.GetFullPath(projectDirectory);
        if (!Directory.Exists(root)) return [];

        return new FileSystemEnumerable<string>(
            root,
            static (ref entry) => entry.ToFullPath(),
            ConeEnumeration)
        {
            ShouldIncludePredicate = static (ref entry) =>
                !entry.IsDirectory && entry.FileName.EndsWith(".cs", StringComparison.Ordinal),
            ShouldRecursePredicate = static (ref entry) =>
                !BuildOutputDirectories.IsBuildOutputName(entry.FileName)
        };
    }

    /// <summary>
    ///     The cone's <c>*.cs</c> that <paramref name="known" /> does not already hold, ordinal-sorted — the
    ///     SDK-glob adds a stat sweep cannot see. Sorted so a caller keying on the result gets the same answer
    ///     whatever order the file system enumerated.
    /// </summary>
    public static IReadOnlyList<string> Adds(string projectDirectory, ICollection<string> known)
    {
        List<string> adds = Enumerate(projectDirectory)
            .Where(full => !known.Contains(full))
            .ToList();
        adds.Sort(StringComparer.Ordinal);
        return adds;
    }

    /// <summary>
    ///     The first cone <c>*.cs</c> that <paramref name="known" /> does not hold, or null when there is
    ///     none — the same question <see cref="Adds" /> answers, for a caller that needs only a witness.
    /// </summary>
    /// <remarks>
    ///     Stops the walk at the first hit, where <see cref="Adds" /> must read the whole cone before it can
    ///     sort it. "First" is therefore first-enumerated rather than first-ordinal: the two differ only in
    ///     <em>which</em> new file a notice names when several appeared at once, and any caller that needs
    ///     the stable choice is asking <see cref="Adds" />.
    /// </remarks>
    public static string? FirstAdd(string projectDirectory, ICollection<string> known)
    {
        return Enumerate(projectDirectory)
            .FirstOrDefault(full => !known.Contains(full));
    }

    /// <summary>
    ///     Every non-source file whose state changes what a build of the project in
    ///     <paramref name="projectDirectory" /> produces: every place the layout in force could put its
    ///     <see cref="IntermediateOutputTree.AssetsPathsOf">restore assets file</see>, then each ancestor
    ///     directory crossed with
    ///     <see cref="FileStamping.StructuralProbeFileNames">every probe name</see> — absent ones included,
    ///     because a probe that later appears is itself the change.
    /// </summary>
    /// <remarks>
    ///     The composition, not just its primitives, is what the consumers must agree on: they are
    ///     deciding whether a cached model is still valid, and a probe one of them stamps and another does not
    ///     is a stale answer served confidently, with nothing red. The order is the order stamps are recorded
    ///     in, so a persisted stamp list keeps its shape. A whole solution's set is
    ///     <see cref="SolutionStructuralPaths" />; this is the per-project course of it, which the warm
    ///     session records directly because it stamps as it walks rather than composing a list.
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
    ///     One solution's whole structural set, deduplicated and in stamping order: the solution file, the
    ///     solution a filter points at, then each project's file followed by its
    ///     <see cref="StructuralPaths" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Both persisted caches stamp this set and both decide "the structure moved" from it, so the
    ///         composition is shared for the reason <see cref="StructuralPaths" /> is: a path one store
    ///         watches and the other does not is a cached model that outlives its inputs on one path and not
    ///         the other, with nothing to show for it.
    ///     </para>
    ///     <para>
    ///         The filter leg costs a non-filter solution nothing — the format test comes before any I/O —
    ///         and is unreachable for the build capture, which refuses a <c>.slnf</c> at ingest. It stays
    ///         unconditional anyway, because the alternative is a parameter that exists only to be passed
    ///         the same way twice.
    ///     </para>
    /// </remarks>
    /// <param name="solutionPath">The solution (or solution-filter) file the run was pointed at.</param>
    /// <param name="projects">
    ///     Each project's file path, directory, and evaluated output pair, in the order they are to be
    ///     stamped.
    /// </param>
    public static IReadOnlyList<string> SolutionStructuralPaths(
        string solutionPath,
        IEnumerable<(string CsprojPath, string ProjectDirectory, string? EvaluatedOutputPath, string?
            IntermediateAssemblyPath)> projects)
    {
        var paths = new List<string>();
        var seen = new HashSet<string>(PathComparison.Comparer);

        string fullSolution = Path.GetFullPath(solutionPath);
        Add(fullSolution);

        // Under a .slnf the file above is the filter, not the solution. Editing the solution changes what a
        // run loads — adding a member the filter selects, or any member at all when its projects array is
        // empty — while leaving the filter's own bytes and timestamp untouched, so without this the edit
        // never dirties the cache and the stale answer is served indefinitely.
        if (SolutionProjectFileParser.TryReadReferencedSolution(fullSolution) is { } referencedSolution)
            Add(referencedSolution);

        foreach ((string csprojPath, string projectDirectory, string? evaluatedOutputPath,
                     string? intermediateAssemblyPath) in projects)
        {
            Add(csprojPath);

            foreach (string path in StructuralPaths(projectDirectory, evaluatedOutputPath, intermediateAssemblyPath))
                Add(path);
        }

        return paths;

        void Add(string path)
        {
            if (seen.Add(path)) paths.Add(path);
        }
    }

    /// <summary>
    ///     Yields <paramref name="startDirectory" /> and each ancestor directory up to the filesystem root.
    ///     The probe chain walks the whole way up rather than stopping at the solution directory: MSBuild
    ///     honors a <c>Directory.Build.props</c>/<c>.targets</c> or <c>global.json</c> found <em>above</em> the
    ///     solution too, so a stamp chain that stopped at the solution directory would miss one and let it flip
    ///     silently. The extra levels are absent-file stamps, which are cheap.
    /// </summary>
    private static IEnumerable<string> Ancestors(string startDirectory)
    {
        string? directory = startDirectory;
        while (directory is not null)
        {
            yield return directory;
            directory = Path.GetDirectoryName(directory);
        }
    }
}
