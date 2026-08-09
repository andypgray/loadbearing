namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The CLI's "you named something that is not there" refusals. Three commands raise one — an
///     unmatched <c>check --rules</c> filter, an unknown <c>explain</c> rule ID, an unmatched
///     <c>graph --projects</c> filter — and a reader who meets two of them should meet one format: name
///     what did not match, then list what was available, so the next command is one edit away. Each
///     caller keeps its own lead and its own label; the shape they share lives here, as does the ordinal
///     sort that makes a rule-ID list read the same on every machine.
/// </summary>
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
}
