using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     Places each frozen scope's directory context file (R3). For every containment rule it
///     evaluates the raw frozen selection in <see cref="SelectionPosition.Subject" /> position (so it
///     ranges over solution-declared types), collects those types' declaration-site file paths, and
///     picks their <em>deepest common ancestor directory</em> — the directory whose <c>AGENTS.md</c>
///     receives the scope card. A scope that matches no types resolves to a null directory with a
///     skip reason. This is the one placement concern that needs the codebase; it stays in Core so it
///     can use the internal <see cref="SelectionEvaluator" />, and the CLI sees only the public result.
/// </summary>
public static class ScopedContextResolver
{
    /// <summary>Resolves a placement for every frozen scope in the model, in model order.</summary>
    public static IReadOnlyList<ScopePlacement> Resolve(ArchitectureModel model, CodebaseModel codebase)
    {
        Guard.NotNull(model, nameof(model));
        Guard.NotNull(codebase, nameof(codebase));

        var evaluator = new SelectionEvaluator(codebase);
        var placements = new List<ScopePlacement>();

        foreach (ArchRule rule in model.Rules)
        {
            if (rule.Freeze is not { Role: FreezeRole.Containment, Frozen: { } frozen } freeze) continue;

            var sites = evaluator.Evaluate(frozen, SelectionPosition.Subject)
                .Where(type => !type.IsExternal)
                .SelectMany(type => type.DeclarationSites)
                .Select(site => site.FilePath)
                .Distinct()
                .ToList();

            placements.Add(sites.Count == 0
                ? new ScopePlacement(freeze.ScopeId, rule, null,
                    $"scope '{freeze.ScopeId}' matched no types; no scoped context emitted")
                : new ScopePlacement(freeze.ScopeId, rule, DeepestCommonDirectory(sites), null));
        }

        return placements;
    }

    // The deepest directory that contains every declaration site: the longest common prefix of the
    // sites' directory segments, compared Ordinal (all paths come from one extraction pass, so casing
    // is consistent — no OS-conditional comparer, per the R3 gotcha). The prefix is reconstructed from
    // the first path so its original root and separators survive verbatim.
    private static string DeepestCommonDirectory(IReadOnlyList<string> filePaths)
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