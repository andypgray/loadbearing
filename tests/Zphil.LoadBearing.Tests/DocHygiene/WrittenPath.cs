namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     One write-report line lifted from a fenced code block in a hand-written doc. <c>render</c> and
///     <c>baseline</c> report every file they touch as <c>wrote &lt;path&gt;</c> or
///     <c>unchanged &lt;path&gt;</c>, and a walkthrough quotes those lines to show what the verb produced.
///     <see cref="Doc" /> and <see cref="DocLine" /> locate the quote so a failing gate names the exact
///     place the stranded path lives.
/// </summary>
/// <param name="Doc">The repository-relative path of the doc the line was quoted in.</param>
/// <param name="DocLine">The 1-based line in the doc the quote sits on.</param>
/// <param name="Label">The report verb the line opens with (<c>wrote</c> or <c>unchanged</c>).</param>
/// <param name="Path">The quoted path, relative to the solution the verb ran against.</param>
internal sealed record WrittenPath(string Doc, int DocLine, string Label, string Path);
