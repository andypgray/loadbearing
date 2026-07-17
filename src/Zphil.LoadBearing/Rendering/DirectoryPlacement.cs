namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     The shared directory-collapse arithmetic for scoped context placement (R3): given the
///     declaration-site file paths of a selection's types, pick their
///     <em>
///         deepest common ancestor
///         directory
///     </em>
///     — the directory whose <c>AGENTS.md</c> receives that selection's card. Both
///     <see cref="ScopedContextResolver" /> (frozen scopes) and <see cref="LayerContextResolver" />
///     (layer local-rules cards) collapse their sites through this one helper, so the two emission
///     keys land a co-located card in exactly the same directory.
/// </summary>
internal static class DirectoryPlacement
{
    // The deepest directory that contains every declaration site: the longest common prefix of the
    // sites' directory segments, compared Ordinal (all paths come from one extraction pass, so casing
    // is consistent — no OS-conditional comparer, per the R3 gotcha). The prefix is reconstructed from
    // the first path so its original root and separators survive verbatim.
    internal static string DeepestCommonDirectory(IReadOnlyList<string> filePaths)
    {
        var segmentLists = filePaths.Select(DirectorySegments).ToList();
        int common = segmentLists[0].Count;
        for (var i = 1; i < segmentLists.Count; i++) common = CommonPrefixLength(segmentLists[0], segmentLists[i], common);

        char separator = filePaths[0].IndexOf('\\') >= 0 ? '\\' : '/';
        return string.Join(separator.ToString(), segmentLists[0].Take(common));
    }

    // A path's directory portion as segments: split on both separators, drop the file name (last
    // segment). Empties are kept so a leading separator (POSIX absolute paths) survives as a leading
    // empty segment that rejoins to a leading separator.
    private static IReadOnlyList<string> DirectorySegments(string path)
    {
        string[] segments = path.Split('/', '\\');
        return segments.Take(segments.Length - 1).ToList();
    }

    private static int CommonPrefixLength(IReadOnlyList<string> a, IReadOnlyList<string> b, int max)
    {
        int limit = Math.Min(max, Math.Min(a.Count, b.Count));
        var length = 0;
        while (length < limit && string.Equals(a[length], b[length], StringComparison.Ordinal)) length++;

        return length;
    }
}