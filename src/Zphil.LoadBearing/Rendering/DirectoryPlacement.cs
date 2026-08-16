using Zphil.LoadBearing.Checking;

namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     Scoped context placement, end to end: given a selection, where does its card go? The whole step —
///     collecting the solution-declared types' declaration-site file paths, then collapsing those to their
///     <em>deepest common ancestor directory</em> — the directory whose <c>AGENTS.md</c> receives that
///     selection's card.
/// </summary>
/// <remarks>
///     Both <see cref="ScopedContextResolver" /> (quarantined scopes) and
///     <see cref="LayerContextResolver" /> (layer local-rules cards) place through this one helper, so the
///     two emission keys land a co-located card in exactly the same directory. Sharing only the arithmetic
///     would leave the more valuable half — <em>which</em> paths get collapsed — free to diverge, with the
///     shared collapse still faithfully collapsing two different inputs.
/// </remarks>
internal static class DirectoryPlacement
{
    /// <summary>
    ///     The directory <paramref name="selection" />'s card belongs in, or null when no solution-declared
    ///     type it matches has a declaration site — the caller's cue to record its own skip reason.
    /// </summary>
    internal static string? ResolveDirectory(SelectionEvaluator evaluator, Selection selection)
    {
        List<string> sites = evaluator.Evaluate(selection, SelectionPosition.Subject)
            .Where(type => !type.IsExternal)
            .SelectMany(type => type.DeclarationSites)
            .Select(site => site.FilePath)
            .Distinct()
            .ToList();

        return sites.Count == 0 ? null : DeepestCommonDirectory(sites);
    }

    // The deepest directory that contains every declaration site: the longest common prefix of the
    // sites' directory segments, compared Ordinal (all paths come from one extraction pass, so casing
    // is consistent — no OS-conditional comparer needed). The prefix is reconstructed from
    // the first path so its original root and separators survive verbatim.
    internal static string DeepestCommonDirectory(IReadOnlyList<string> filePaths)
    {
        // Only the first path's segments are ever read back, and the answer can only shrink, so the rest
        // are split one at a time and abandoned — and the walk stops the moment two paths share no root
        // at all, however many sites are still queued behind them.
        string[] prefix = filePaths[0].Split('/', '\\');
        int common = DirectorySegmentCount(prefix);
        for (var i = 1; i < filePaths.Count && common > 0; i++)
        {
            string[] segments = filePaths[i].Split('/', '\\');
            int directories = DirectorySegmentCount(segments);
            common = CommonPrefixLength(prefix, segments, directories, common);
        }

        char separator = filePaths[0].IndexOf('\\') >= 0 ? '\\' : '/';
        return string.Join(separator.ToString(), prefix.Take(common));
    }

    // A split path's directory portion is everything but the file name (the last segment). Empties are
    // kept so a leading separator (POSIX absolute paths) survives as a leading empty segment that rejoins
    // to a leading separator.
    private static int DirectorySegmentCount(string[] segments)
    {
        return segments.Length - 1;
    }

    // Both bounds are directory-segment counts, so the file names sitting past them are never compared.
    private static int CommonPrefixLength(string[] a, string[] b, int bDirectorySegments, int max)
    {
        int limit = Math.Min(max, bDirectorySegments);
        var length = 0;
        while (length < limit && string.Equals(a[length], b[length], StringComparison.Ordinal)) length++;

        return length;
    }
}
