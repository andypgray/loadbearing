using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Roslyn.Solutions;

/// <summary>
///     Where a project's intermediate (<c>obj</c>-side) tree is, and where the restore assets file inside it
///     is, derived from the two paths MSBuild evaluated for the project rather than from a layout the code
///     assumes. Shared by the spec resolver's built-output search — which must never return an intermediate
///     assembly — and by the three cache probes, which must stamp the assets file wherever the layout in
///     force actually writes it.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why derived rather than spelled.</b> The default layout puts the assets file at
///         <c>&lt;projectDirectory&gt;/obj/project.assets.json</c>. <c>UseArtifactsOutput</c> puts it at
///         <c>&lt;artifacts&gt;/obj/&lt;Project&gt;/project.assets.json</c> — outside the project directory
///         entirely — and a redirected <c>BaseIntermediateOutputPath</c> puts it anywhere. A probe that
///         spells the default layout finds nothing under the others, and an absent probe is not a quiet
///         degradation: it is a structural input the cache stops watching, so a restore that changes the
///         model is served from cache as though nothing moved — measured on the skew matrix, not read off
///         the code.
///     </para>
///     <para>
///         <b>Why a chain rather than one path.</b> Between the intermediate root and the assembly sit the
///         build pivots — configuration, target framework, runtime identifier — and how many there are is
///         the layout's business, not this code's: the default layout puts the assets file three levels
///         above the assembly and the artifacts layout one. Every directory on that walk is therefore
///         stamped, absent ones included, which is what makes an assets file that appears on a later restore
///         an existence flip rather than a file nobody was watching. The extras are absent-file stats, the
///         same cheap shape the props/targets probe chain already relies on.
///     </para>
/// </remarks>
internal static class IntermediateOutputTree
{
    /// <summary>The NuGet restore assets file's name — the structural input this tree is probed for.</summary>
    internal const string AssetsFileName = "project.assets.json";

    /// <summary>
    ///     The project's restore assets file in the <em>default</em> output layout
    ///     (<c>&lt;projectDirectory&gt;/obj/project.assets.json</c>) — the first, and always present,
    ///     candidate <see cref="AssetsPathsOf" /> yields.
    /// </summary>
    /// <remarks>
    ///     Spelled here and nowhere else, because a second spelling of it is a layout assumption living
    ///     outside the type that owns the derivation, free to drift from it. The extraction cache's
    ///     per-project content key names this one path rather than the whole candidate set, which is sound
    ///     for the narrow reason that the key is a manifest-diff aid and not the invalidation mechanism: an
    ///     assets file that moves or changes is a structural change, and the structural sweep misses the
    ///     whole cache before any content key is recomputed.
    /// </remarks>
    /// <param name="projectDirectory">The directory holding the project file.</param>
    internal static string DefaultAssetsPathOf(string projectDirectory)
    {
        return Path.GetFullPath(Path.Combine(projectDirectory, "obj", AssetsFileName));
    }

    /// <summary>
    ///     The root of the project's intermediate tree, derived by peeling the shared prefix off the
    ///     evaluated and intermediate assembly paths — so the rule never has to name <c>obj</c>, and holds
    ///     for a <c>BaseIntermediateOutputPath</c> redirected anywhere. Null when the two paths share no
    ///     path root, or when the intermediate path adds no directory of its own (it is the evaluated path,
    ///     or a sibling of it, or unknown).
    /// </summary>
    internal static string? RootOf(string evaluatedOutputPath, string? intermediateAssemblyPath)
    {
        if (string.IsNullOrWhiteSpace(intermediateAssemblyPath) || !Path.IsPathRooted(intermediateAssemblyPath))
            return null;
        if (string.IsNullOrWhiteSpace(evaluatedOutputPath) || !Path.IsPathRooted(evaluatedOutputPath)) return null;

        // Path.GetFullPath, not PathCanonicalizer.Resolve: both paths come from one MSBuild evaluation of one
        // project so they already share a spelling, and a symlink walk on the failure path costs syscalls per
        // ancestor while risking a root spelled differently from the anchor.
        string evaluated = Path.GetFullPath(evaluatedOutputPath);
        string intermediate = Path.GetFullPath(intermediateAssemblyPath);

        // Peeling the path root off both is what makes UNC and drive-relative input safe with no
        // leading-empty-segment special case: two different roots share nothing worth comparing.
        string evaluatedRoot = Path.GetPathRoot(evaluated) ?? "";
        string intermediateRoot = Path.GetPathRoot(intermediate) ?? "";
        if (!string.Equals(evaluatedRoot, intermediateRoot, PathComparison.Comparison)) return null;

        string[] evaluatedSegments = SegmentsBelow(evaluated, evaluatedRoot);
        string[] intermediateSegments = SegmentsBelow(intermediate, intermediateRoot);

        var shared = 0;
        while (shared < evaluatedSegments.Length
               && shared < intermediateSegments.Length
               && string.Equals(evaluatedSegments[shared], intermediateSegments[shared], PathComparison.Comparison))
            shared++;

        // One guard collapses every degenerate case: the intermediate path equal to the evaluated one, the
        // same directory under a different file name, and an intermediate assembly sitting directly beside
        // the output. In each the intermediate tree adds no directory of its own, so there is nothing to
        // exclude and excluding the shared directory would refuse the real output.
        if (shared >= intermediateSegments.Length - 1) return null;

        return Path.Combine([evaluatedRoot, .. intermediateSegments.Take(shared + 1)]);
    }

    /// <summary>
    ///     Every path the project's <c>project.assets.json</c> could occupy under the layout in force: the
    ///     default <c>&lt;projectDirectory&gt;/obj/</c> location first, then — when the project's evaluated
    ///     paths are both known — each directory from the intermediate assembly's own directory up to and
    ///     including <see cref="RootOf">the intermediate root</see>. Ordered, deduplicated, absolute.
    /// </summary>
    /// <remarks>
    ///     The default location leads whatever the layout, so the common case keeps the order it has always
    ///     had and a project whose evaluated paths are unknown — a binlog replay carries no intermediate
    ///     assembly path — degrades to exactly the set that was stamped before this derivation existed.
    ///     One framework's pair is enough for a multi-target-framework project: the root is derived from the
    ///     prefix the pair shares, and that root is the same whichever framework's pair it starts from.
    /// </remarks>
    /// <param name="projectDirectory">The directory holding the project file.</param>
    /// <param name="evaluatedOutputPath">The project's evaluated output path, or null when unknown.</param>
    /// <param name="intermediateAssemblyPath">The project's intermediate assembly path, or null when unknown.</param>
    internal static IReadOnlyList<string> AssetsPathsOf(
        string projectDirectory, string? evaluatedOutputPath, string? intermediateAssemblyPath)
    {
        var paths = new List<string>();
        var seen = new HashSet<string>(PathComparison.Comparer);

        Add(DefaultAssetsPathOf(projectDirectory));

        if (evaluatedOutputPath is null || RootOf(evaluatedOutputPath, intermediateAssemblyPath) is not { } root)
            return paths;

        string? directory = Path.GetDirectoryName(Path.GetFullPath(intermediateAssemblyPath!));
        while (!string.IsNullOrEmpty(directory))
        {
            Add(Path.Combine(directory, AssetsFileName));

            // The root is an ancestor-or-self of the assembly's directory by construction, so this is the
            // walk's terminator; the null check below is only the degenerate-input backstop.
            if (string.Equals(directory, root, PathComparison.Comparison)) break;

            directory = Path.GetDirectoryName(directory);
        }

        return paths;

        void Add(string path)
        {
            if (seen.Add(path)) paths.Add(path);
        }
    }

    private static string[] SegmentsBelow(string fullPath, string pathRoot)
    {
        return fullPath.Substring(pathRoot.Length)
            .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
    }
}
