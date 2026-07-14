namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     A managed <c>realpath</c>: resolves a path to a symlink-free, fully-qualified spelling so that
///     paths of different provenance can be compared. <c>git rev-parse --show-toplevel</c> returns a
///     canonical (symlink-resolved) path, but <see cref="System.IO.Path.GetFullPath(string)" /> — and
///     MSBuildWorkspace's document paths — keep whatever spelling the solution was opened with. On a
///     symlinked root (macOS's <c>/var</c> → <c>/private/var</c>, a symlinked home, a Windows junction)
///     the two disagree, and the Freeze tripwire's prefix match silently misses. Canonicalizing once at
///     the discovery seam makes the git-derived and workspace-derived paths agree.
/// </summary>
/// <remarks>
///     Lives in <c>.Roslyn</c> (net-current) rather than Core, because
///     <see cref="System.IO.DirectoryInfo.ResolveLinkTarget(bool)" /> is net6+ and Core is
///     netstandard2.0. The resolution is a fixed-point walk: <see cref="System.IO.Path.GetFullPath(string)" />
///     first, then repeatedly find the deepest symlinked ancestor, follow it to its final target, and
///     reattach the remainder until no symlink remains. It is a no-op on ordinary (non-symlinked) paths
///     and falls back to <c>GetFullPath</c> when the path does not exist — callers keep their own
///     existence checks.
/// </remarks>
public static class PathCanonicalizer
{
    // Belt-and-braces bound so a pathological reparse graph (mutually recursive symlinks) can never
    // spin forever; real filesystems converge in one or two passes. A genuine cycle throws inside
    // ResolveLinkTarget and is swallowed as "not a resolvable link", so this is only a backstop.
    private const int MaxIterations = 40;

    /// <summary>
    ///     Returns <paramref name="path" /> made absolute and symlink-free. A no-op on non-symlinked
    ///     paths; falls back to <see cref="System.IO.Path.GetFullPath(string)" /> on nonexistent input.
    /// </summary>
    public static string Resolve(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;

        string current = Path.GetFullPath(path);
        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            string? resolved = ResolveDeepestSymlink(current);
            if (resolved is null) return current; // no symlinked ancestor left — fixed point reached

            current = Path.GetFullPath(resolved);
        }

        return current;
    }

    // Walks ancestors leaf→root; at the first (deepest) symlinked ancestor, returns the path with that
    // ancestor replaced by its final target and the remainder reattached. Returns null when no ancestor
    // is a symlink.
    private static string? ResolveDeepestSymlink(string current)
    {
        for (DirectoryInfo? dir = new(current); dir is not null; dir = dir.Parent)
        {
            if (TryResolveLinkTarget(dir) is not { } target) continue;

            string remainder = Path.GetRelativePath(dir.FullName, current);
            return remainder == "." ? target : Path.Combine(target, remainder);
        }

        return null;
    }

    // ResolveLinkTarget probes the path as a reparse point, which throws on drive roots ("C:\"),
    // access-denied ancestors, and nonexistent paths; a throw here just means "not a resolvable link,
    // keep walking up".
    private static string? TryResolveLinkTarget(DirectoryInfo dir)
    {
        try
        {
            return dir.ResolveLinkTarget(true)?.FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}