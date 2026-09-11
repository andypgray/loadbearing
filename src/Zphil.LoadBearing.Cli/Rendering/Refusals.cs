using Zphil.LoadBearing.Hosting;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     The CLI's "you named something that is not there" refusals — an unmatched filter, an unknown ID. A
///     reader who meets two of them should meet one format: name what did not match, then list what was
///     available, so the next command is one edit away. Each caller keeps its own lead; the shape they share
///     lives here, as does the label-plus-roster pairing the two rule-ID refusals share and the ordinal sort
///     that makes a rule-ID list read the same on every machine.
/// </summary>
/// <remarks>
///     One value never earns the roster: a JSON array a client serialized into the string parameter it was
///     binding. Every name the reader wants is already inside their own brackets, so answering with the whole
///     spec's is a menu to a question nobody asked — the defect is the shape, not the spelling.
///     <see cref="StringifiedArrayElements" /> recognizes it and <see cref="StringifiedArrayMessage" /> names
///     it instead. The refusals are shared by the CLI and the MCP tools by construction, so no wording here may
///     name a flag or a tool parameter: it names the shape both surfaces pass.
/// </remarks>
internal static class Refusals
{
    /// <summary>Every rule ID in <paramref name="model" />, ordinal-sorted — the post-desugar spec as a reader can name it.</summary>
    internal static IEnumerable<string> AvailableRuleIds(ArchitectureModel model)
    {
        return model.Rules.Select(rule => rule.Id).OrderBy(id => id, StringComparer.Ordinal);
    }

    /// <summary>
    ///     The refusal text: <paramref name="lead" /> (what did not match), then
    ///     <paramref name="label" /> introducing <paramref name="available" />, one indented name per
    ///     line.
    /// </summary>
    internal static string NotFoundMessage(string lead, string label, IEnumerable<string> available)
    {
        return $"{lead}. {label}:\n  " + string.Join("\n  ", available);
    }

    /// <summary>
    ///     A named rule that is not in <paramref name="model" />: <paramref name="lead" /> over the spec's
    ///     whole rule roster. The label and the roster travel together, so <c>check</c>'s unmatched
    ///     <c>--rules</c> filter and <c>explain</c>'s unknown ID differ only in what they lead with.
    /// </summary>
    internal static string RuleNotFound(string lead, ArchitectureModel model)
    {
        return NotFoundMessage(lead, "Available rule IDs", AvailableRuleIds(model));
    }

    /// <summary>
    ///     The elements of a JSON array that reached the CLI as text — <c>["a","b"]</c> read as <c>a</c> and
    ///     <c>b</c>, quotes of either kind stripped — or <c>null</c> when <paramref name="value" /> is not one.
    /// </summary>
    /// <remarks>
    ///     Brackets are unambiguous here: a rule ID is dash-and-slash lowercase segments and a project name
    ///     carries dots, so nothing legitimate opens with <c>[</c>. Deliberately a reader, never a repair — the
    ///     coercer above it lets a string that merely looks like an array through verbatim, so a caller's
    ///     literal value survives untouched and is refused rather than rewritten into something they did not
    ///     ask for. The empty array is still an array, and comes back as an empty list rather than
    ///     <c>null</c>, so it is refused on its shape like every other one.
    /// </remarks>
    internal static IReadOnlyList<string>? StringifiedArrayElements(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '[' || trimmed[^1] != ']') return null;

        return trimmed[1..^1]
            .Split(',')
            .Select(element => element.Trim()
                .Trim('"', '\''))
            .Where(element => element.Length > 0)
            .ToList();
    }

    /// <summary>
    ///     The refusal text for a stringified array: <paramref name="lead" /> (what did not match), then the
    ///     shape, then <paramref name="advice" /> — the caller's own sentence for what to pass instead.
    /// </summary>
    /// <remarks>
    ///     Lead and advice, and no roster. A caller whose brackets hold an ID that does not exist pays one
    ///     extra round trip for that — shape first, roster on the retry — which is the trade: the shape is
    ///     always the defect worth naming first, and the message stays one sentence.
    /// </remarks>
    internal static string StringifiedArrayMessage(string lead, string advice)
    {
        return $"{lead}. That is a JSON array written as text; {advice}.";
    }

    /// <summary>
    ///     The advice both glob-list filters give — one semicolon-separated string, echoing
    ///     <paramref name="elements" /> as that string, which is the value the caller can paste back.
    /// </summary>
    internal static string GlobListAdvice(IReadOnlyList<string> elements)
    {
        const string advice = "pass the globs as one semicolon-separated string";
        return elements.Count == 0 ? advice : $"{advice}: '{string.Join(";", elements)}'";
    }

    /// <summary>
    ///     The advice every scalar parameter gives — <paramref name="advice" /> naming what one of them is,
    ///     echoing <paramref name="elements" /> only when it holds exactly one value.
    /// </summary>
    /// <remarks>
    ///     The echo is withheld for an array of several deliberately: it holds no single value the verb could
    ///     have taken, so naming one would be picking for the caller. The glob twin above can always echo,
    ///     because its parameter accepts the whole list as one string.
    /// </remarks>
    internal static string SingleValueAdvice(string advice, IReadOnlyList<string> elements)
    {
        return elements.Count == 1 ? $"{advice}: '{elements[0]}'" : advice;
    }
}
