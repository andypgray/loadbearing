namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     Renders source paths solution-relative with forward slashes — the machine-independent form both
///     render targets emit. This is what keeps the JSON golden pin and the human acceptance box stable
///     across machines.
/// </summary>
/// <remarks>
///     <c>Path.GetRelativePath</c> does not exist on netstandard2.0, so the relative
///     walk is hand-rolled to match its semantics: full-path both operands, compare directory segments
///     with the platform's file-name comparison (case-insensitive on Windows and macOS, ordinal on
///     Linux), and emit <c>../</c> per unmatched base segment followed by the remaining target segments.
///     Different roots (a different drive) fall back to the raw target with slashes normalized.
///     Pinned equivalent to <c>Path.GetRelativePath(dir, file).Replace('\\','/')</c> by
///     <c>PathFormatTests</c>.
/// </remarks>
public static class PathFormat
{
    /// <summary>The forward-slashed path from <paramref name="solutionDirectory" /> to <paramref name="filePath" />.</summary>
    public static string Relative(string solutionDirectory, string filePath)
    {
        return new Relativizer(solutionDirectory).Relative(filePath);
    }

    /// <summary>
    ///     Whether <paramref name="directory" /> equals or is an ancestor of <paramref name="path" /> — the
    ///     scope-placement question: does this directory's context card cover that file?
    /// </summary>
    /// <remarks>
    ///     Both operands are full-pathed and split into segments, and the segments are compared with the
    ///     same per-OS rule <see cref="Relative" /> uses (<see cref="PathComparison" />), so containment
    ///     and relativization cannot disagree about whether two spellings are one path. Symlinks are not
    ///     resolved: a caller that needs canonical paths canonicalizes before asking.
    /// </remarks>
    public static bool Contains(string directory, string path)
    {
        string[] directorySegments = Segments(Path.GetFullPath(directory));
        string[] pathSegments = Segments(Path.GetFullPath(path));
        if (directorySegments.Length > pathSegments.Length) return false;

        for (var i = 0; i < directorySegments.Length; i++)
            if (!string.Equals(directorySegments[i], pathSegments[i], PathComparison.Comparison))
                return false;

        return true;
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

    /// <summary><see cref="Relative" /> with its base directory analyzed once.</summary>
    /// <remarks>
    ///     The base is the same string for every site of a report, and full-pathing plus splitting it per
    ///     call is the whole constant half of the walk — so a renderer builds one of these and relativizes
    ///     each site against it. There is only ever one algorithm: <see cref="Relative" /> runs this one.
    /// </remarks>
    public sealed class Relativizer
    {
        private readonly string[] _baseSegments;

        /// <summary>Creates a relativizer rooted at <paramref name="baseDirectory" />.</summary>
        public Relativizer(string baseDirectory)
        {
            _baseSegments = Segments(Path.GetFullPath(baseDirectory));
        }

        /// <summary>The forward-slashed path from the base directory to <paramref name="filePath" />.</summary>
        public string Relative(string filePath)
        {
            string[] toSegments = Segments(Path.GetFullPath(filePath));

            // Different roots (a different drive) have no relative path: fall back to the raw target with
            // slashes normalized.
            if (_baseSegments.Length == 0 || toSegments.Length == 0 ||
                !string.Equals(_baseSegments[0], toSegments[0], PathComparison.Comparison))
                return filePath.Replace('\\', '/');

            var common = 0;
            int limit = Math.Min(_baseSegments.Length, toSegments.Length);
            while (common < limit && string.Equals(_baseSegments[common], toSegments[common], PathComparison.Comparison)) common++;

            var parts = new List<string>();
            for (int i = common; i < _baseSegments.Length; i++) parts.Add("..");
            for (int i = common; i < toSegments.Length; i++) parts.Add(toSegments[i]);

            // Empty means the target IS the directory — Path.GetRelativePath returns "." there.
            return parts.Count == 0 ? "." : string.Join("/", parts);
        }
    }
}
