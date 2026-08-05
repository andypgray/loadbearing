using System.Text.RegularExpressions;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     Extra hygiene patterns read from an untracked file at the repository root, added to the tracked
///     shape patterns when the tree-wide gate runs.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why these are not in source.</b> Some things a working copy must never publish can only
///         be matched by name — a private sibling repository has no shape to recognize it by. A
///         denylist that spells those names out would publish them permanently, in a public file
///         whose whole subject is that they are private, which costs more than most leaks it would
///         catch. So the tracked catalog carries shapes, and names live here.
///     </para>
///     <para>
///         <b>What absence means.</b> A fresh clone and every leg of CI have no such file, and the
///         gate is still whole without it: the tracked shapes are the enforced contract, and this is
///         a local strengthening on top of them for the working copy a release is cut from. That is
///         why a missing file yields no patterns rather than failing.
///     </para>
///     <para>
///         The format is one .NET regex per line, matched case-insensitively; blank lines and lines
///         beginning <c>#</c> are ignored.
///     </para>
/// </remarks>
internal static class LocalPrivatePatterns
{
    // The untracked file's name, at the repository root.
    private const string FileName = ".hygiene-private";

    private static readonly Lazy<Regex[]> LazyPatterns = new(Load);

    /// <summary>The patterns the file declares, or none when it is absent.</summary>
    public static IReadOnlyList<Regex> Patterns => LazyPatterns.Value;

    private static string FilePath => Path.Combine(RepoRoot.Directory, FileName);

    private static Regex[] Load()
    {
        if (!File.Exists(FilePath)) return [];

        return File.ReadAllLines(FilePath)
            .Select(static line => line.Trim())
            .Where(static line => line.Length > 0 && !line.StartsWith('#'))
            .Select(static line => new Regex(line, RegexOptions.IgnoreCase))
            .ToArray();
    }
}
