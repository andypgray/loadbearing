using System.Text.RegularExpressions;
using System.Xml.Linq;
using Zphil.LoadBearing.Rendering;

namespace Zphil.LoadBearing.Roslyn.Replay;

/// <summary>
///     Reads a solution file's declared <c>.csproj</c> membership <em>textually</em>, with no MSBuild — the
///     ground truth <see cref="BinlogCaptureStore" />'s coverage check compares a replayed binlog against, so
///     it can refuse a binlog that does not build exactly the solution's project set. Handles
///     both the classic <c>.sln</c> and the XML <c>.slnx</c> format; non-<c>.csproj</c> entries (solution
///     folders, shared projects, database projects) are ignored, and both slash spellings resolve.
/// </summary>
/// <remarks>
///     This is a coverage oracle, not a solution loader: it only needs the project <em>paths</em>, so it does
///     not evaluate configurations, conditions, or nested-project ownership. Paths are made absolute against
///     the solution file's directory but not symlink-canonicalized — the store canonicalizes both sides at
///     comparison time (matching <c>SpecResolver.PathsEqual</c>), so this stays pure and disk-independent for
///     its <see cref="ParseCsprojMembers" /> core.
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