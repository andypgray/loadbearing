namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     One rule-header line lifted from a fenced code block in a hand-written doc. A doc quotes captured
///     <c>check</c> output, and each stanza opens with the rule's status, its id, and the sentence the
///     spec renders for it — the same sentence <c>render</c> writes into <c>AGENTS.md</c>.
///     <see cref="Doc" /> and <see cref="DocLine" /> locate the quote so a failing gate names the exact
///     place the stale sentence lives.
/// </summary>
/// <param name="Doc">The repository-relative path of the doc the line was quoted in.</param>
/// <param name="DocLine">The 1-based line in the doc the quote sits on.</param>
/// <param name="Status">The status verb the line opens with (<c>pass</c>, <c>FAIL</c>, <c>warn</c> or <c>skip</c>).</param>
/// <param name="RuleId">The quoted rule id, which may carry more than two segments for a scoped rule.</param>
/// <param name="Sentence">The rendered rule sentence quoted after the em dash.</param>
internal sealed record RuleQuote(string Doc, int DocLine, string Status, string RuleId, string Sentence);
