namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     The build-output directory names (<c>bin</c>, <c>obj</c>) that both cache probes exclude, defined once
///     so the on-disk <see cref="ProjectCone">cone scan</see> and the in-memory
///     <see cref="SolutionCacheInputs">document filter</see> cannot drift on what counts as a build artifact.
/// </summary>
/// <remarks>
///     The two consumers apply the rule through different mechanisms, so the shared thing is the directory
///     <em>names</em>, not one matcher. <see cref="ProjectCone" /> walks the disk with
///     <c>Microsoft.Extensions.FileSystemGlobbing</c> and consumes <see cref="ExcludeGlobs" />. The
///     <see cref="SolutionCacheInputs" /> filter runs over an in-memory list of absolute Roslyn document paths
///     and must keep a document that lives <em>above</em> the project directory (a
///     <c>&lt;Compile Include="..\Shared\X.cs"&gt;</c> link), so it uses <see cref="IsUnderBuildOutput" /> — a
///     relative-segment test the globbing library's root-scoped matcher cannot reproduce, because that matcher
///     only enumerates paths under its root and would silently drop an above-root link.
/// </remarks>
internal static class BuildOutputDirectories
{
    // Case-sensitive by design: the historical relative-segment skip matched "bin"/"obj" ordinally, so a
    // differently-cased directory (e.g. "BIN") was never treated as build output. Kept exactly.
    private static readonly string[] Names = ["bin", "obj"];

    /// <summary>
    ///     Ant-style glob excludes (<c>**/bin/**</c>, <c>**/obj/**</c>) for a FileSystemGlobbing
    ///     <c>Matcher</c> whose root is the project directory.
    /// </summary>
    internal static IEnumerable<string> ExcludeGlobs => Names.Select(name => $"**/{name}/**");

    /// <summary>
    ///     True when <paramref name="path" />, taken relative to <paramref name="projectDirectory" />, crosses
    ///     a build-output segment. A path above the project directory yields a <c>..</c>-prefixed relative path
    ///     with no such segment, so a linked source file outside the cone stays tracked.
    /// </summary>
    internal static bool IsUnderBuildOutput(string projectDirectory, string path)
    {
        string relative = Path.GetRelativePath(projectDirectory, path).Replace('\\', '/');
        return relative.Split('/').Any(segment => Names.Contains(segment));
    }
}