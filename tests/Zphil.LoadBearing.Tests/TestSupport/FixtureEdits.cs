using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     Mutates a fixture tree the way the suites that exercise reconciliation and caching need it mutated:
///     an edit the freshness machinery is guaranteed to notice, or a tree stamped old enough to be trusted.
/// </summary>
/// <remarks>
///     <para>
///         <b>The freshness contract every method here is written against.</b> Both the workspace sweep and
///         the persisted extraction cache decide what to re-read by comparing a file's mtime against the one
///         recorded when it was last seen, and a file stamped close to now is treated as racy — re-read
///         rather than trusted. So the two directions are stamped deliberately, and neither number is
///         arbitrary.
///     </para>
///     <para>
///         <b>Forward, +2s.</b> An edit stamps the future, which guarantees the sweep sees a delta against
///         the load-time fingerprint whatever the filesystem's timestamp granularity — an edit stamped
///         "now" can land inside the same tick as the load and read as unchanged.
///     </para>
///     <para>
///         <b>Backward, -1d.</b> Backdating puts every input comfortably outside the racy window, so the
///         next capture stamps it promoted — the precondition for the stat fast path. It is best-effort
///         throughout: a file that cannot be re-stamped stays racy and simply re-reads, which costs a read
///         and no correctness.
///     </para>
/// </remarks>
internal static class FixtureEdits
{
    /// <summary>Rewrites a file through <paramref name="transform" />, stamping the edit forward.</summary>
    internal static void EditOnDisk(string path, Func<string, string> transform)
    {
        string content = File.ReadAllText(path);
        File.WriteAllText(path, transform(content));
        // A future mtime guarantees the reconcile sweep sees a delta against the load-time fingerprint.
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(2));
    }

    /// <summary>
    ///     Splices <paramref name="member" /> into the (single) class in a source file, just before its
    ///     closing brace, leaving everything after that brace as it was — line-ending agnostic (it only
    ///     anchors on the final <c>}</c>), so a CRLF or LF fixture checkout both work. The member carries
    ///     its own surrounding newlines.
    /// </summary>
    internal static void InsertMember(string path, string member)
    {
        string original = File.ReadAllText(path);
        int lastBrace = original.LastIndexOf('}');
        File.WriteAllText(path, original[..lastBrace] + member + original[lastBrace..]);
    }

    /// <summary>
    ///     Appends <paramref name="member" /> as its own line before a class's final closing brace, then
    ///     re-closes the class — so the member needs no newlines of its own and any trailing text after the
    ///     brace is replaced.
    /// </summary>
    internal static void AppendMemberLine(string path, string member)
    {
        File.WriteAllText(path, SpliceMemberLine(File.ReadAllText(path), member));
    }

    /// <summary>
    ///     <see cref="AppendMemberLine" /> as a source transform, for handing to <see cref="EditOnDisk" />
    ///     where the edit must also be stamped forward.
    /// </summary>
    internal static string SpliceMemberLine(string source, string member)
    {
        int lastBrace = source.LastIndexOf('}');
        return source[..lastBrace] + member + "\n}\n";
    }

    /// <summary>
    ///     Stamps every document in <paramref name="snapshot" /> well into the past, so the next sweep
    ///     promotes them instead of re-reading.
    /// </summary>
    internal static void BackdateAllDocuments(WorkspaceSnapshot snapshot)
    {
        DateTime wellPast = DateTime.UtcNow.AddDays(-1);
        IEnumerable<string> paths = snapshot.Solution.Projects
            .SelectMany(p => p.Documents)
            .Select(d => d.FilePath)
            .OfType<string>()
            .Distinct();

        foreach (string path in paths) Backdate(path, wellPast);
    }

    /// <summary>
    ///     Stamps every file under <paramref name="root" /> well into the past — the persisted-cache analog of
    ///     <see cref="BackdateAllDocuments" />, over a directory tree rather than a loaded solution.
    ///     <paramref name="excluding" /> skips a path prefix, which is how a cache directory living inside the
    ///     tree keeps its real write times.
    /// </summary>
    internal static void BackdateTree(string root, string? excluding = null)
    {
        DateTime wellPast = DateTime.UtcNow.AddDays(-1);
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (excluding is not null && file.StartsWith(excluding, PathComparison.Comparison)) continue;
            Backdate(file, wellPast);
        }
    }

    private static void Backdate(string path, DateTime wellPast)
    {
        try
        {
            File.SetLastWriteTimeUtc(path, wellPast);
        }
        catch (IOException)
        {
            // Best-effort: a file we cannot re-stamp stays racy and simply re-reads, which costs a read.
        }
    }
}
