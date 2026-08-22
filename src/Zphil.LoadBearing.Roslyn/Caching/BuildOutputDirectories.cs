namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     The build-output directory names (<c>bin</c>, <c>obj</c>) that both cache probes exclude, defined once
///     so the on-disk <see cref="ProjectCone">cone scan</see> and the in-memory
///     <see cref="SolutionCacheInputs">document filter</see> cannot drift on what counts as a build artifact.
/// </summary>
/// <remarks>
///     The two consumers ask at different moments, so what they share is the names and the comparison rather
///     than one matcher. <see cref="ProjectCone" /> prunes a live disk walk by directory name and never
///     descends. The <see cref="SolutionCacheInputs" /> filter runs over an in-memory list of absolute Roslyn
///     document paths and must keep a document that lives <em>above</em> the project directory (a
///     <c>&lt;Compile Include="..\Shared\X.cs"&gt;</c> link), so it reads the relative path instead — which a
///     root-scoped disk walk could never express, because it never leaves its root.
/// </remarks>
internal static class BuildOutputDirectories
{
    // Matched ordinally, deliberately: a differently-cased directory (e.g. "BIN") is not treated as build
    // output.
    private static readonly string[] Names = ["bin", "obj"];

    /// <summary>
    ///     Whether <paramref name="directoryName" /> names a build-output directory — the ordinal test both
    ///     probes reduce to. Takes a span so the disk walk can ask it of a directory entry without
    ///     materializing the name.
    /// </summary>
    internal static bool IsBuildOutputName(ReadOnlySpan<char> directoryName)
    {
        foreach (string name in Names)
            if (directoryName.Equals(name, StringComparison.Ordinal))
                return true;

        return false;
    }

    /// <summary>
    ///     True when <paramref name="path" />, taken relative to <paramref name="projectDirectory" />, crosses
    ///     a build-output segment. A path above the project directory yields a <c>..</c>-prefixed relative path
    ///     with no such segment, so a linked source file outside the cone stays tracked.
    /// </summary>
    /// <remarks>
    ///     Walked as spans over the single relative path it has to materialize — hot path: asked once per
    ///     document per project on every cold load, the largest input any cache probe faces.
    /// </remarks>
    internal static bool IsUnderBuildOutput(string projectDirectory, string path)
    {
        ReadOnlySpan<char> remaining = Path.GetRelativePath(projectDirectory, path);
        while (!remaining.IsEmpty)
        {
            int separator = remaining.IndexOfAny('\\', '/');
            ReadOnlySpan<char> segment = separator < 0 ? remaining : remaining[..separator];
            if (IsBuildOutputName(segment)) return true;
            if (separator < 0) break;

            remaining = remaining[(separator + 1)..];
        }

        return false;
    }
}
