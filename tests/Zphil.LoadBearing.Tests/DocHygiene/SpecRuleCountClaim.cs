namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     One prose claim about how many rules govern this repository, lifted from a hand-written doc. The
///     count is carried as the doc writes it — a spelled word or a numeral — so the gate can say what the
///     page claims beside what the rendered board holds. <see cref="Doc" /> and <see cref="DocLine" />
///     locate the claim so a failing gate names the exact sentence to correct.
/// </summary>
/// <param name="Doc">The repository-relative path of the doc the claim was written in.</param>
/// <param name="DocLine">The 1-based line in the doc the claim sits on.</param>
/// <param name="Written">The count exactly as the prose spells it, word or numeral.</param>
internal sealed record SpecRuleCountClaim(string Doc, int DocLine, string Written);
