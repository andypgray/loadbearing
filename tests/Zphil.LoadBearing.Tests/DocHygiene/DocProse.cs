using System.Text.RegularExpressions;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     The prose-hygiene checker the documentation gates run on. It measures prose that sits
///     outside fenced code blocks — fenced blocks quote tool output, whose idiom belongs to the
///     tool rather than to the house voice — and it finds references the published documentation
///     is meant to stay free of. All check logic lives here so the gate tests and the negative
///     tests exercise the same code path.
/// </summary>
internal static class DocProse
{
    private static readonly Regex NonWhitespaceRun = new(@"\S+");

    private static readonly Regex TicWords =
        new("deliberately|intentionally", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Returns <paramref name="text" /> with every fenced code block removed.</summary>
    /// <remarks>
    ///     The opening and closing fence lines and everything between them are removed, and an unclosed
    ///     fence removes everything to the end of the text. Where a fence opens and closes is
    ///     <see cref="SourceAnchors.ProseLines" />'s decision — the quote gates and the prose gates read
    ///     one scanner, or a doc could be fenced for one of them and not the other.
    /// </remarks>
    public static string StripFences(string text)
    {
        IEnumerable<string> kept = SourceAnchors.ProseLines(text)
            .Select(static line => line.Text);

        return string.Join("\n", kept);
    }

    /// <summary>Counts whitespace-separated tokens (runs of non-whitespace characters).</summary>
    public static int CountWords(string text)
    {
        return NonWhitespaceRun.Matches(text)
            .Count;
    }

    /// <summary>Counts em-dash (U+2014) occurrences.</summary>
    public static int CountEmDashes(string text)
    {
        return text.Count(static character => character == (char)0x2014);
    }

    /// <summary>Counts case-insensitive occurrences of the house tic words.</summary>
    public static int CountTics(string text)
    {
        return TicWords.Matches(text)
            .Count;
    }

    /// <summary>
    ///     Finds every match of <paramref name="patterns" /> in <paramref name="text" />, each formatted
    ///     <c>"{lineNumber}: {matchedText}"</c> with 1-based line numbers over the text as given, so a
    ///     failing gate names every offending line.
    /// </summary>
    public static IReadOnlyList<string> FindForbidden(string text, IEnumerable<Regex> patterns)
    {
        Regex[] patternList = patterns as Regex[] ?? patterns.ToArray();
        string normalized = text.NormalizedLines();

        Regex[] present = patternList.Where(pattern => CanSkipWholeText(pattern) || pattern.IsMatch(normalized))
            .ToArray();
        if (present.Length == 0) return [];

        string[] lines = normalized.Split('\n');
        List<string> hits = new();

        for (var index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            foreach (Regex pattern in present)
            foreach (Match match in pattern.Matches(line))
                hits.Add($"{index + 1}: {match.Value}");
        }

        return hits;
    }

    // A pattern that matches somewhere in a line also matches the text those lines came from, so one
    // pass over the whole text is a sound filter for the per-line pass — and the callers that scan a
    // repository run hundreds of patterns over tens of thousands of lines that hit nothing at all.
    // The exception is an anchored pattern: without Multiline, '^' and '$' mean the ends of the text
    // rather than the ends of a line, so those skip the filter and are always located line by line.
    private static bool CanSkipWholeText(Regex pattern)
    {
        var source = pattern.ToString();

        return source.Contains('^') || source.Contains('$');
    }
}
