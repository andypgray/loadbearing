namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The CLI's "you named something that is not there" refusals — an unmatched filter, an unknown ID. A
///     reader who meets two of them should meet one format: name what did not match, then list what was
///     available, so the next command is one edit away. Each caller keeps its own lead; the shape they share
///     lives here, as does the label-plus-roster pairing the two rule-ID refusals share and the ordinal sort
///     that makes a rule-ID list read the same on every machine.
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

    /// <summary>
    ///     A named rule that is not in <paramref name="model" />: <paramref name="lead" /> over the spec's
    ///     whole rule roster. The label and the roster travel together, so <c>check</c>'s unmatched
    ///     <c>--rules</c> filter and <c>explain</c>'s unknown ID differ only in what they lead with.
    /// </summary>
    internal static string RuleNotFound(string lead, ArchitectureModel model)
    {
        return NotFoundMessage(lead, "Available rule IDs", AvailableRuleIds(model));
    }
}
