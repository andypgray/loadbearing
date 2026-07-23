using System.Text.RegularExpressions;

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

    /// <summary>
    ///     Returns <paramref name="text" /> with every fenced code block removed. A fence opens on a
    ///     line whose first non-whitespace content is a run of three or more backticks or tildes; it
    ///     closes on the next line that, after optional leading whitespace, is a run of at least as many
    ///     of the same fence character with nothing after it but whitespace. The opening and closing
    ///     lines and everything between them are removed, and an unclosed fence removes everything to
    ///     the end of the text. Input newlines are normalized to <c>"\n"</c> first.
    /// </summary>
    public static string StripFences(string text)
    {
        string normalized = text.Replace("\r\n", "\n");
        string[] lines = normalized.Split('\n');
        List<string> kept = new();
        var insideFence = false;
        var fenceChar = '\0';
        var fenceLength = 0;

        foreach (string line in lines)
            if (!insideFence)
            {
                if (TryOpenFence(line, out fenceChar, out fenceLength))
                    insideFence = true;
                else
                    kept.Add(line);
            }
            else if (ClosesFence(line, fenceChar, fenceLength))
            {
                insideFence = false;
                fenceChar = '\0';
                fenceLength = 0;
            }

        return string.Join("\n", kept);
    }

    /// <summary>Counts whitespace-separated tokens (runs of non-whitespace characters).</summary>
    public static int CountWords(string text)
    {
        return NonWhitespaceRun.Matches(text).Count;
    }

    /// <summary>Counts em-dash (U+2014) occurrences.</summary>
    public static int CountEmDashes(string text)
    {
        return text.Count(static character => character == (char)0x2014);
    }

    /// <summary>Counts case-insensitive occurrences of the house tic words.</summary>
    public static int CountTics(string text)
    {
        return TicWords.Matches(text).Count;
    }

    /// <summary>
    ///     Finds every match of <paramref name="patterns" /> in <paramref name="text" />, each formatted
    ///     <c>"{lineNumber}: {matchedText}"</c> with 1-based line numbers over the text as given, so a
    ///     failing gate names every offending line.
    /// </summary>
    public static IReadOnlyList<string> FindForbidden(string text, IEnumerable<Regex> patterns)
    {
        var patternList = patterns as Regex[] ?? patterns.ToArray();
        string normalized = text.Replace("\r\n", "\n");
        string[] lines = normalized.Split('\n');
        List<string> hits = new();

        for (var index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            foreach (Regex pattern in patternList)
            foreach (Match match in pattern.Matches(line))
                hits.Add($"{index + 1}: {match.Value}");
        }

        return hits;
    }

    private static bool TryOpenFence(string line, out char fenceChar, out int fenceLength)
    {
        fenceChar = '\0';
        fenceLength = 0;
        int index = SkipLeadingWhitespace(line);
        if (index >= line.Length) return false;

        char candidate = line[index];
        if (candidate != '`' && candidate != '~') return false;

        int run = RunLength(line, index, candidate);
        if (run < 3) return false;

        fenceChar = candidate;
        fenceLength = run;
        return true;
    }

    private static bool ClosesFence(string line, char fenceChar, int minimumLength)
    {
        int index = SkipLeadingWhitespace(line);
        int run = RunLength(line, index, fenceChar);
        if (run < minimumLength) return false;

        int afterRun = index + run;
        while (afterRun < line.Length)
        {
            if (line[afterRun] != ' ' && line[afterRun] != '\t') return false;

            afterRun++;
        }

        return true;
    }

    private static int SkipLeadingWhitespace(string line)
    {
        var index = 0;
        while (index < line.Length && (line[index] == ' ' || line[index] == '\t')) index++;

        return index;
    }

    private static int RunLength(string line, int start, char character)
    {
        int index = start;
        while (index < line.Length && line[index] == character) index++;

        return index - start;
    }
}