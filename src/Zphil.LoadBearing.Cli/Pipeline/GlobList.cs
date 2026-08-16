namespace Zphil.LoadBearing.Cli.Pipeline;

/// <summary>
///     Parses the semicolon-separated glob list every filtering option on this CLI takes — the MSBuild
///     list idiom. Blanks are dropped, so a trailing separator is not a pattern that matches nothing, and
///     an absent option (null) parses to the empty list every filter reads as "narrow nothing". One parser
///     so two options cannot disagree about what a list is.
/// </summary>
internal static class GlobList
{
    /// <summary>The globs in <paramref name="value" />, trimmed, in the order they were written.</summary>
    public static IReadOnlyList<string> Parse(string? value)
    {
        return value is null
            ? []
            : value.Split(';').Select(glob => glob.Trim()).Where(glob => glob.Length > 0).ToList();
    }
}
