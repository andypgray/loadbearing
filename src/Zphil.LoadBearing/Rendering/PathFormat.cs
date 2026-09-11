using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     Renders source paths relative to the solution directory with forward slashes: the form the check
///     report, <c>--json</c> output and SARIF output all carry, so a path reads the same whichever
///     machine produced it.
/// </summary>
/// <remarks>
///     <see cref="Relative" /> gives what <c>Path.GetRelativePath(directory, file)</c> gives with
///     backslashes replaced by forward ones. Both arguments are made absolute first; segments are
///     compared the way the platform compares file names, case-insensitively on Windows and macOS and
///     ordinally on Linux; a target above the base directory is reached with <c>../</c> segments; a
///     target that is the base directory itself comes back as <c>.</c>; and two paths with different
///     roots, a different drive say, have no relative form at all, so the target comes back as it was
///     with its slashes normalized.
/// </remarks>
// Path.GetRelativePath does not exist on netstandard2.0, so the walk is hand-rolled; PathFormatTests
// pins it equivalent to Path.GetRelativePath(dir, file).Replace('\\','/'), which is what keeps the
// JSON golden and the pinned human report stable across machines.
public static class PathFormat
{
    /// <summary>
    ///     The forward-slashed path from <paramref name="solutionDirectory" /> to
    ///     <paramref name="filePath" />. Relativizing many paths against one directory is cheaper through a
    ///     <see cref="Relativizer" />, which analyses the directory once.
    /// </summary>
    public static string Relative(string solutionDirectory, string filePath)
    {
        return new Relativizer(solutionDirectory).Relative(filePath);
    }

    /// <summary>
    ///     Whether <paramref name="directory" /> is <paramref name="path" /> or an ancestor of it: the
    ///     question of whether the card placed on a directory covers a given file.
    /// </summary>
    /// <remarks>
    ///     Both arguments are made absolute and compared segment by segment, the way the platform compares
    ///     file names, so this and <see cref="Relative" /> cannot disagree about whether two spellings are
    ///     one path. Symlinks are not resolved: canonicalize first if that matters.
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

    /// <summary>
    ///     A base directory analysed once, for a caller relativizing many paths against the same one: a
    ///     report renderer with a site per violation, say. Otherwise
    ///     <c>PathFormat.Relative(directory, file)</c> answers the same question in one call.
    /// </summary>
    // One algorithm, not two: PathFormat.Relative builds one of these and calls it.
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
