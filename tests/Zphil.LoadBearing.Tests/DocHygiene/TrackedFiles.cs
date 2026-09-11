using Zphil.LoadBearing.Roslyn.Hosting;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     The set of files this repository publishes — everything git tracks — split into the C# sources
///     and the other text files, with binaries sniffed out.
/// </summary>
/// <remarks>
///     <para>
///         Git decides the scope. What the hygiene gates check is that nothing internal ships
///         publicly, and tracked-by-git is precisely what ships; every other definition of the set
///         approximates it — through a hand-written exclusion list whose going stale is the very
///         defect these gates exist to catch. Tracked-by-git also excludes the gitignored private
///         working layer and every build output without a list to maintain, and it cannot grow a
///         blind spot the day someone adds a top-level directory. When git is missing this throws
///         rather than yielding nothing, because a gate that silently scans an empty set is worse
///         than no gate.
///     </para>
///     <para>
///         Plain <c>ls-files</c>, not <c>-z</c>: <see cref="ChildProcess.Run" />'s capture is
///         line-oriented and LF-normalized, which is exactly what plain <c>ls-files</c> emits. The
///         quoting that <c>-z</c> exists to solve is instead pinned absent by a fact over the path
///         character set, so the day a path needs quoting the gate says so rather than silently
///         dropping the file.
///     </para>
///     <para>
///         The enumeration is forced at pipeline startup — <see cref="FixtureRestoreStartup" /> calls
///         <see cref="EnsureEnumerated" /> before any test runs — because the spawn is a redirected
///         child process, and fired lazily from whichever gate reads <see cref="All" /> first it would
///         race the workspace-loading tests in <c>"Serial"</c>: the deadlock shape
///         <see cref="SerialCollection" />'s remarks document.
///     </para>
/// </remarks>
internal static class TrackedFiles
{
    // How much of a file the text/binary sniff reads — the same window `git grep -I` looks at.
    private const int SniffBytes = 8192;

    private static readonly Lazy<IReadOnlyList<string>> LazyAll = new(Enumerate);

    private static readonly Lazy<IReadOnlyList<string>> LazyCSharp =
        new(() => LazyAll.Value.Where(IsCSharp)
            .ToArray());

    private static readonly Lazy<IReadOnlyList<string>> LazyNonSourceText =
        new(() => LazyAll.Value.Where(static path => !IsCSharp(path))
            .Where(IsText)
            .ToArray());

    private static readonly Lazy<IReadOnlyList<string>> LazyMarkdown =
        new(() => LazyAll.Value.Where(IsMarkdown)
            .ToArray());

    /// <summary>Every tracked path, repository-relative with forward slashes, as git reports it.</summary>
    public static IReadOnlyList<string> All => LazyAll.Value;

    /// <summary>The tracked C# sources — the set the comment mask is run over.</summary>
    public static IReadOnlyList<string> CSharp => LazyCSharp.Value;

    /// <summary>
    ///     The tracked text files that are not C# sources. Read whole, because a settings file, a
    ///     workflow or an ignore rule has no construct that separates prose from data.
    /// </summary>
    public static IReadOnlyList<string> NonSourceText => LazyNonSourceText.Value;

    /// <summary>
    ///     The tracked markdown — the hand-written docs, which is the set every quote gate sweeps for a
    ///     doc nobody registered.
    /// </summary>
    public static IReadOnlyList<string> Markdown => LazyMarkdown.Value;

    /// <summary>
    ///     Forces the tracked-file inventory now, swallowing any failure: <see cref="Lazy{T}" /> caches
    ///     a faulted enumeration, so a missing git still throws at the first gate that reads
    ///     <see cref="All" />, naming itself. Surfacing the fault here instead would turn "the DocHygiene
    ///     gates fail" into "the whole run fails to start".
    /// </summary>
    internal static void EnsureEnumerated()
    {
        try
        {
            _ = LazyAll.Value;
        }
        catch (Exception)
        {
            // Deliberately swallowed — the cached fault re-throws at the first gate that reads the set.
        }
    }

    private static bool IsCSharp(string relativePath)
    {
        return relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMarkdown(string relativePath)
    {
        return relativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
    }

    // Text or binary the way `git grep -I` decides it — a NUL byte near the start means binary. An
    // extension allow-list would carry the same drift a named-roots walk does: the extension nobody
    // thinks to add is scanned by nothing.
    private static bool IsText(string relativePath)
    {
        using FileStream stream = File.OpenRead(RepoRoot.Absolute(relativePath));
        var head = new byte[SniffBytes];
        int read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);

        return head.AsSpan(0, read)
            .IndexOf((byte)0) < 0;
    }

    private static IReadOnlyList<string> Enumerate()
    {
        return GitCommand.Output(RepoRoot.Directory, "ls-files")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
