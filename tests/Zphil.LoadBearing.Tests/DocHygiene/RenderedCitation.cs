namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     One citation as a committed context file renders it: the page a rule's <c>Citation</c> names,
///     lifted out of the managed block's rule bullet. <see cref="Doc" /> and <see cref="DocLine" /> locate
///     the rendered bullet so a failing gate names the exact place the dead page reaches an agent, and
///     <see cref="RuleId" /> names the rule whose <c>Citation</c> to fix in the spec behind it.
/// </summary>
/// <param name="Doc">The repository-relative path of the context file the citation is rendered in.</param>
/// <param name="DocLine">The 1-based line in that file the rendered bullet sits on.</param>
/// <param name="RuleId">The rule id the bullet opens with — the spec's handle on the citation.</param>
/// <param name="Url">The page the bullet autolinks.</param>
internal sealed record RenderedCitation(string Doc, int DocLine, string RuleId, string Url);
