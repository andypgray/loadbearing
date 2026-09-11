using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Roslyn.Solutions;

namespace Zphil.LoadBearing.Cli.Pipeline;

/// <summary>
///     Finds the spec assembly a build actually wrote, for the case where the output path MSBuild evaluated
///     names a file that is not on disk. The search is anchored inside the SDK's own output root, bounded in
///     depth, and — where the project's intermediate assembly path is known — structurally cannot return an
///     intermediate (<c>obj</c>-side) assembly.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why a search rather than arithmetic.</b> An evaluated output path can name a directory no
///         build ever writes: a parent props file can set the output path before the SDK defaults the
///         properties it interpolates (evaluating to a flat <c>bin\</c>), <c>UseArtifactsOutput</c>
///         relocates the whole tree, and a solution built in one configuration evaluates in another.
///         Fixed hop counting over such a path climbs past the project or re-appends a segment that is
///         not a target framework, because it assumes a shape it did not construct.
///     </para>
///     <para>
///         <b>The named output root is the ceiling of the walk, never the anchor.</b> The walk starts at the
///         evaluated path's own directory and rises no higher than the nearest ancestor named <c>bin</c> or
///         <c>artifacts</c>. The search anchors at the deepest directory in that walk that exists and widens
///         toward the ceiling only while it finds nothing — a directory's existence is no evidence of a
///         build, because the design-time build the workspace load runs creates the evaluated directory
///         itself, empty, before resolution ever reads the disk (measured live). Anchoring deep is what
///         keeps <c>artifacts/bin/&lt;OtherProject&gt;/…</c> out of scope whenever the project's own
///         subtree holds any candidate, instead of making it a ranking problem.
///     </para>
///     <para>
///         <b>Why the intermediate refusal is not already implied by that.</b> Under the SDK's own layouts
///         the anchor rule alone keeps <c>obj</c> out of scope — it is never under <c>bin</c> or
///         <c>artifacts/bin</c>. The refusal is what makes "never an intermediate assembly" hold
///         structurally in <em>any</em> layout rather than incidentally in those: it demonstrably fires
///         when <c>BaseIntermediateOutputPath</c> is redirected under the output root, which is the tree
///         the negative-control test builds.
///     </para>
/// </remarks>
internal static class BuiltOutputProbe
{
    // The two output-root spellings the .NET SDK owns: OutputPath's default `bin\` and ArtifactsPath's
    // default `artifacts\`. They bound the walk; they are never the anchor. A repository that renames its
    // output root gets the primary resolution and no cross-configuration fallback — the deliberate trade for
    // a search that can never climb to a repository or drive root.
    private static readonly string[] OutputRootNames = ["bin", "artifacts"];

    // Directories holding a same-named copy that must never be the answer:
    //   ref / refint — reference assemblies. Metadata only, no IL bodies, so loading one as the spec
    //                  fails inside Define() rather than at load.
    //   publish      — `dotnet publish` output, possibly trimmed or stale.
    //   runtimes     — RID-specific assets. Defensive rather than measured: the spec assembly's own
    //                  name is unlikely there, but the cost of being wrong is high.
    // A deny list, because no allow list is possible — configuration names, target frameworks and RIDs are
    // all user-defined. The cost is that a future SDK directory has to be added here.
    private static readonly string[] ExcludedSegments = ["ref", "refint", "publish", "runtimes"];

    // Four levels is the deepest output shape the SDK itself produces — bin/<platform>/<config>/<tfm>/<rid>/
    // — so five is that plus one level of slack. The bound is also the cycle guard: an anchor under a
    // junction pointing back up its own tree terminates here.
    private const int MaxSearchDepth = 5;

    /// <summary>
    ///     The built assembly matching <paramref name="evaluatedOutputPath" />'s file name, searched for
    ///     under the output root that path sits in, or <see langword="null" /> when there is none to find.
    /// </summary>
    /// <param name="evaluatedOutputPath">
    ///     The output path MSBuild evaluated for the project. Must be rooted: an unrooted path would resolve
    ///     against the current working directory, which is never what an MSBuild evaluation meant.
    /// </param>
    /// <param name="intermediateAssemblyPath">
    ///     The project's intermediate (<c>obj</c>-side) assembly path when it is known, so the result can
    ///     never be an intermediate assembly. Null or blank is "unknown", not an error — the binlog replay
    ///     path has no such path to give.
    /// </param>
    internal static string? Find(string evaluatedOutputPath, string? intermediateAssemblyPath)
    {
        if (string.IsNullOrWhiteSpace(evaluatedOutputPath) || !Path.IsPathRooted(evaluatedOutputPath)) return null;

        string assemblyFileName = Path.GetFileName(evaluatedOutputPath);
        string? intermediateRoot = IntermediateOutputTree.RootOf(evaluatedOutputPath, intermediateAssemblyPath);

        // AttributesToSkip is left at its Hidden | System default deliberately: nothing a build means to be
        // loaded is marked either, and the default keeps a recursive walk out of system directories.
        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = MaxSearchDepth,
            IgnoreInaccessible = true
        };

        foreach (string anchor in AnchorChainFor(evaluatedOutputPath))
        {
            // Shallowest first (the canonical output beats a nested RID copy), then most recently written (the
            // sibling-configuration rule the release-CI pin rests on), then ordinal path so ties are stable.
            string? found = Directory.EnumerateFiles(anchor, assemblyFileName, enumeration)
                .Select(Path.GetFullPath)
                .Where(candidate => !CrossesExcludedSegment(anchor, candidate))
                .Where(candidate => !IsUnderIntermediateRoot(candidate, intermediateRoot))
                .OrderBy(candidate => DepthBelow(anchor, candidate))
                .ThenByDescending(File.GetLastWriteTimeUtc)
                .ThenBy(candidate => candidate, StringComparer.Ordinal)
                .FirstOrDefault();

            if (found is not null) return found;
        }

        return null;
    }

    /// <summary>
    ///     Every directory the search may anchor at, deepest first: the existing directories on the walk from
    ///     <paramref name="evaluatedOutputPath" />'s own directory up to and including the nearest ancestor
    ///     named <c>bin</c> or <c>artifacts</c>. Empty when no such ancestor exists or when nothing on the
    ///     walk was ever created.
    /// </summary>
    /// <remarks>
    ///     A chain rather than one anchor because the deepest existing directory can be an empty shell the
    ///     design-time build created, with the real build one level up; widening only while the search
    ///     finds nothing keeps another project's same-named output out of scope whenever this project's
    ///     own subtree yields any candidate.
    /// </remarks>
    internal static IReadOnlyList<string> AnchorChainFor(string evaluatedOutputPath)
    {
        string? directory = Path.GetDirectoryName(evaluatedOutputPath);
        if (string.IsNullOrEmpty(directory)) return [];

        var chain = new List<string>();
        for (string? current = directory; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            chain.Add(current);
            if (IsOutputRootDirectory(current))
                return chain
                    .Where(Directory.Exists)
                    .ToList();
        }

        return [];
    }

    private static bool IsOutputRootDirectory(string directory)
    {
        string name = Path.GetFileName(directory);
        return OutputRootNames.Any(root => string.Equals(name, root, PathComparison.Comparison));
    }

    // The same anchor-relative segment shape BuildOutputDirectories.IsUnderBuildOutput uses, matched with
    // PathComparison rather than that type's ordinal rule — deliberately. There a false positive drops a
    // real source document, so narrow is safe; here a false negative loads a reference assembly as the
    // spec, so broad is safe.
    private static bool CrossesExcludedSegment(string anchor, string candidatePath)
    {
        return RelativeSegments(anchor, candidatePath)
            .Any(segment => ExcludedSegments.Contains(segment, PathComparison.Comparer));
    }

    // The trailing separator is load-bearing: a bare prefix test would also reject `<proj>/obj2/…`.
    private static bool IsUnderIntermediateRoot(string candidatePath, string? intermediateRoot)
    {
        if (intermediateRoot is null) return false;

        string prefix = intermediateRoot + Path.DirectorySeparatorChar;
        return candidatePath.StartsWith(prefix, PathComparison.Comparison);
    }

    private static int DepthBelow(string anchor, string candidatePath)
    {
        return RelativeSegments(anchor, candidatePath)
            .Length;
    }

    private static string[] RelativeSegments(string anchor, string candidatePath)
    {
        return SplitSegments(Path.GetRelativePath(anchor, candidatePath));
    }

    private static string[] SplitSegments(string path)
    {
        return path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
    }
}
