namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     Renders source paths solution-relative with forward slashes — the machine-independent form both
///     render targets emit (precedent: <c>WorkspaceFixture.RelativePath</c>). This is what keeps the JSON
///     golden pin and the human acceptance box stable across machines. Lives in Core (netstandard2.0), so
///     the CLI, the MCP tools, and the xUnit adapter all share one path-formatting rule.
/// </summary>
/// <remarks>
///     <c>Path.GetRelativePath</c> does not exist on netstandard2.0, so the relative
///     walk is hand-rolled to match its semantics: full-path both operands, compare directory segments
///     with the platform's file-name comparison (case-insensitive on Windows and macOS, ordinal on
///     Linux), and emit <c>../</c> per unmatched base segment followed by the remaining target segments.
///     Different roots (a different drive) fall back to the raw target with slashes normalized — the same
///     behavior the CLI's former <c>Path.GetRelativePath</c> wrapper produced from its <c>catch</c>.
///     Pinned equivalent to <c>Path.GetRelativePath(dir, file).Replace('\\','/')</c> by
///     <c>PathFormatTests</c>.
/// </remarks>
public static class PathFormat
{
    /// <summary>The forward-slashed path from <paramref name="solutionDirectory" /> to <paramref name="filePath" />.</summary>
    public static string Relative(string solutionDirectory, string filePath)
    {
        string[] fromSegments = Segments(Path.GetFullPath(solutionDirectory));
        string[] toSegments = Segments(Path.GetFullPath(filePath));

        // Different roots (a different drive) have no relative path: fall back to the raw target with
        // slashes normalized — the CLI wrapper's former catch behavior.
        if (fromSegments.Length == 0 || toSegments.Length == 0 ||
            !string.Equals(fromSegments[0], toSegments[0], PathComparison.Comparison))
            return filePath.Replace('\\', '/');

        var common = 0;
        int limit = Math.Min(fromSegments.Length, toSegments.Length);
        while (common < limit && string.Equals(fromSegments[common], toSegments[common], PathComparison.Comparison)) common++;

        var parts = new List<string>();
        for (int i = common; i < fromSegments.Length; i++) parts.Add("..");
        for (int i = common; i < toSegments.Length; i++) parts.Add(toSegments[i]);

        // Empty means the target IS the directory — Path.GetRelativePath returns "." there.
        return parts.Count == 0 ? "." : string.Join("/", parts);
    }

    // Split a full path into segments, dropping trailing empties from a trailing separator while keeping a
    // leading empty (a POSIX absolute path's root), so two POSIX absolutes still share their empty root.
    private static string[] Segments(string fullPath)
    {
        string[] parts = fullPath.Split('/', '\\');
        int end = parts.Length;
        while (end > 0 && parts[end - 1].Length == 0) end--;

        return end == parts.Length ? parts : parts.Take(end).ToArray();
    }
}