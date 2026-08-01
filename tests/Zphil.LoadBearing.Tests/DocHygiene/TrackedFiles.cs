using System.Diagnostics;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     The set of files this repository publishes — everything git tracks — split into the C# sources
///     and the other text files, with binaries sniffed out.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why git decides the scope.</b> What the hygiene gates check is that nothing internal
///         ships publicly, and tracked-by-git is precisely what ships; every other definition of the
///         set is an approximation of it. It also excludes the gitignored private working layer and
///         every build output without a list to maintain, and it cannot grow a blind spot the day
///         someone adds a top-level directory.
///     </para>
///     <para>
///         <b>The alternative, considered and rejected:</b> a directory walk from the repository root
///         filtered by a build-output predicate. That needs a hand-written exclusion list for the
///         private roots — and a hand-written list going stale is the very defect these gates exist
///         to catch, the shape of the five-file gate a tree-wide policy outgrew.
///     </para>
///     <para>
///         <b>The cost is one subprocess,</b> already paid on every leg of CI: this project runs git
///         for the diff-base fixtures on all three legs, and the workflow gates on a git command of
///         its own, so git on PATH is already a hard prerequisite of a green suite. When git is
///         missing this throws rather than yielding nothing, because a gate that silently scans an
///         empty set is worse than no gate.
///     </para>
///     <para>
///         <b>Plain <c>ls-files</c>, not <c>-z</c>.</b> <see cref="ChildProcess.Run" />'s capture is
///         line-oriented and LF-normalized, which is exactly what plain <c>ls-files</c> emits. The
///         quoting that <c>-z</c> exists to solve is instead pinned absent by a fact over the path
///         character set, so the day a path needs quoting the gate says so rather than silently
///         dropping the file.
///     </para>
/// </remarks>
internal static class TrackedFiles
{
    // How much of a file the text/binary sniff reads — the same window `git grep -I` looks at.
    private const int SniffBytes = 8192;

    private static readonly Lazy<IReadOnlyList<string>> LazyAll = new(Enumerate);

    private static readonly Lazy<IReadOnlyList<string>> LazyCSharp =
        new(() => LazyAll.Value.Where(IsCSharp).ToArray());

    private static readonly Lazy<IReadOnlyList<string>> LazyNonSourceText =
        new(() => LazyAll.Value.Where(static path => !IsCSharp(path)).Where(IsText).ToArray());

    /// <summary>Every tracked path, repository-relative with forward slashes, as git reports it.</summary>
    public static IReadOnlyList<string> All => LazyAll.Value;

    /// <summary>The tracked C# sources — the set the comment mask is run over.</summary>
    public static IReadOnlyList<string> CSharp => LazyCSharp.Value;

    /// <summary>
    ///     The tracked text files that are not C# sources. Read whole, because a settings file, a
    ///     workflow or an ignore rule has no construct that separates prose from data.
    /// </summary>
    public static IReadOnlyList<string> NonSourceText => LazyNonSourceText.Value;

    /// <summary>The absolute path of a repository-relative tracked path.</summary>
    public static string Absolute(string relativePath)
    {
        return Path.Combine(RepoRoot.Directory, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static bool IsCSharp(string relativePath)
    {
        return relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
    }

    // Text or binary the way `git grep -I` decides it — a NUL byte near the start means binary. An
    // extension allow-list would carry the same drift a named-roots walk does: the extension nobody
    // thinks to add is scanned by nothing.
    private static bool IsText(string relativePath)
    {
        using FileStream stream = File.OpenRead(Absolute(relativePath));
        var head = new byte[SniffBytes];
        int read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);

        return head.AsSpan(0, read).IndexOf((byte)0) < 0;
    }

    // Runs `git -C <root> ls-files`; throws on non-zero exit. Launched through ChildProcess so this
    // git gets the closed stdin, the bounded wait and the kill-tree every child here gets.
    private static IReadOnlyList<string> Enumerate()
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = RepoRoot.Directory,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(RepoRoot.Directory);
        startInfo.ArgumentList.Add("ls-files");

        ChildProcess.ProcessResult result = ChildProcess.Run(startInfo);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"'git ls-files' failed with exit code {result.ExitCode}."
                + $"{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}{result.StandardError}");

        return result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}