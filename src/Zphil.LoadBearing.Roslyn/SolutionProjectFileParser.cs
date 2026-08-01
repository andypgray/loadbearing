using System.Text.RegularExpressions;
using System.Xml.Linq;
using Zphil.LoadBearing.Rendering;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     Reads a solution file's <em>declared</em> <c>.csproj</c> membership textually, with no MSBuild.
///     Handles both the classic <c>.sln</c> and the XML <c>.slnx</c> format; non-<c>.csproj</c> entries
///     (solution folders, shared projects, database projects) are ignored, and both slash spellings resolve.
/// </summary>
/// <remarks>
///     <para>
///         Two consumers, one question — <em>what does the solution declare?</em>
///         <see cref="Replay.BinlogCaptureStore" />'s coverage check compares a replayed binlog against this
///         set so it can refuse a binlog that does not build exactly the solution's projects, and
///         <see cref="SpecExclusion" /> subtracts it from a spec project's <c>ProjectReference</c> closure to
///         tell the spec's private plumbing from the codebase under law. Both need declared membership rather
///         than "every project MSBuild loaded", which is why this lives beside them rather than inside
///         <c>Replay</c>.
///     </para>
///     <para>
///         This is a membership oracle, not a solution loader: it only needs the project <em>paths</em>, so it
///         does not evaluate configurations, conditions, or nested-project ownership. Paths are made absolute
///         against the solution file's directory but not symlink-canonicalized — callers canonicalize both
///         sides at comparison time (matching <c>SpecResolver.PathsEqual</c>), so this stays pure and
///         disk-independent for its <see cref="ParseCsprojMembers" /> core.
///     </para>
/// </remarks>
internal static class SolutionProjectFileParser
{
    // A classic-.sln project line: Project("{TypeGuid}") = "Name", "Relative\Path.csproj", "{ProjectGuid}".
    // The second quoted field (named group "path") is the project path; solution folders put a folder name
    // there instead, filtered out later by the .csproj extension test.
    private static readonly Regex SlnProjectLine = new(
        "Project\\(\"\\{[^}]*\\}\"\\)\\s*=\\s*\"[^\"]*\",\\s*\"(?<path>[^\"]*)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    ///     Whether this parser owns <paramref name="solutionPath" />'s format — the two full-solution
    ///     formats only. A solution filter (<c>.slnf</c>) is JSON that
    ///     <see cref="SolutionDiscovery" /> accepts and the classic-<c>.sln</c> regex reads as <em>zero</em>
    ///     members, so a caller that subtracts declared membership must ask this first rather than mistake an
    ///     unparsed file for "the solution declares nothing".
    /// </summary>
    internal static bool OwnsFormat(string solutionPath)
    {
        string extension = Path.GetExtension(solutionPath);
        return extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Reads <paramref name="solutionPath" /> from disk and returns the absolute paths of its
    ///     <c>.csproj</c> members. Dispatches on the file extension (<c>.slnx</c> ⇒ XML, else classic).
    /// </summary>
    internal static IReadOnlyList<string> ReadCsprojMembers(string solutionPath)
    {
        string fullPath = Path.GetFullPath(solutionPath);
        string text = File.ReadAllText(fullPath);
        string directory = Path.GetDirectoryName(fullPath)!;
        return ParseCsprojMembers(text, Path.GetExtension(fullPath), directory);
    }

    /// <summary>
    ///     The pure text-to-paths core, testable without disk: parses <paramref name="solutionText" /> per
    ///     <paramref name="extension" /> (<c>.slnx</c> ⇒ XML, else classic <c>.sln</c>), keeps only
    ///     <c>.csproj</c> entries, and resolves each against <paramref name="solutionDirectory" />
    ///     (normalizing both slash spellings). Duplicate paths collapse.
    /// </summary>
    internal static IReadOnlyList<string> ParseCsprojMembers(
        string solutionText, string extension, string solutionDirectory)
    {
        var relativePaths = extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase)
            ? ParseSlnx(solutionText)
            : ParseSln(solutionText);

        var results = new List<string>();
        var seen = new HashSet<string>(PathComparison.Comparer);
        foreach (string relative in relativePaths)
        {
            if (!relative.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) continue;

            string normalized = relative.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            string fullPath = Path.GetFullPath(Path.Combine(solutionDirectory, normalized));
            if (seen.Add(fullPath)) results.Add(fullPath);
        }

        return results;
    }

    private static IEnumerable<string> ParseSln(string solutionText)
    {
        foreach (Match match in SlnProjectLine.Matches(solutionText))
            yield return match.Groups["path"].Value;
    }

    private static IEnumerable<string> ParseSlnx(string solutionText)
    {
        // Project entries may sit at the root or nested under <Folder> elements, so walk every descendant.
        return XDocument.Parse(solutionText)
            .Descendants()
            .Where(element => element.Name.LocalName == "Project")
            .Select(element => element.Attribute("Path")?.Value)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!);
    }
}