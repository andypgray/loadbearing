using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     The quote-sync gate over prose claims about how many rules govern this repository. The root README
///     opens its dogfood spine by saying how much law the spec carries, and nothing regenerates that
///     sentence, so this holds the number to the rule bullets the committed <c>AGENTS.md</c> managed block
///     renders: a spec that grows a rule without the README following fails the suite instead of
///     publishing a page that contradicts the board two screens below it.
/// </summary>
/// <remarks>
///     Sibling to <see cref="GrandfatheredCountSyncTests" />, which holds quoted baseline sizes to the
///     committed JSON, and to <see cref="RuleQuoteSyncTests" />, which holds quoted rule sentences to that
///     same board. Like them this reads only committed bytes: no workspace load, no CLI invocation, so it
///     stays cheap and parallel-safe. <see cref="SpecRuleCounts" /> carries why the board is the authority
///     and why this number is not <c>check</c>'s.
/// </remarks>
public sealed class SpecCountSyncTests
{
    private const string RootReadme = "README.md";

    /// <summary>The docs that claim a self-spec rule count, in registration order.</summary>
    private static readonly string[] CountDocs = [RootReadme];

    private static readonly DocGate<SpecRuleCountClaim> Gate = new(
        CountDocs,
        static doc => SpecRuleCounts.Extract(doc, RepoRoot.ReadText(doc)),
        quotes: "self-spec rule counts");

    /// <summary>
    ///     Every claim spells the number of rule bullets the committed board renders. The comparison is
    ///     against the count spelled as a title-case word, which is the form the sentence is written in;
    ///     a claim that moves to a numeral fails here rather than silently passing a looser parse.
    /// </summary>
    [Fact]
    public void EveryClaimedRuleCount_MatchesTheRenderedBoard()
    {
        int rendered = SpecRuleCounts.RenderedRuleCount(File.ReadAllText(RepoRoot.AgentsMd));
        string expected = GrandfatheredCounts.Spell(rendered, GrandfatheredCounts.CountStyle.TitleWord);

        List<string> drift = CountDocs
            .SelectMany(Gate.Scan)
            .Where(claim => !string.Equals(claim.Written, expected, StringComparison.Ordinal))
            .Select(claim =>
                $"{claim.Doc}:{claim.DocLine} claims '{claim.Written} rules' but the board renders {rendered} ('{expected}').")
            .ToList();

        drift.ShouldReportNothing("These docs state a rule count the committed AGENTS.md board contradicts");
    }

    /// <summary>
    ///     The board still yields rule bullets. Without this the gate above passes vacuously the day the
    ///     renderer changes the bullet shape: zero rendered rules would compare against a doc that claims
    ///     a number, and the failure would read as doc drift rather than as a blind scanner.
    /// </summary>
    [Fact]
    public void RenderedBoard_YieldsRuleBullets()
    {
        SpecRuleCounts.RenderedRuleCount(File.ReadAllText(RepoRoot.AgentsMd))
            .ShouldBeGreaterThan(0,
                "the committed AGENTS.md managed block yielded no rule bullets, so the bullet shape has moved "
                + "and every count this gate holds is being compared against zero");
    }

    /// <summary>Every registered doc still yields a claim — the guard against the scanner going blind.</summary>
    [Fact]
    public void EveryRegisteredDoc_YieldsAClaim()
    {
        Gate.ShouldYieldFromEveryRegisteredDoc();
    }

    /// <summary>
    ///     No tracked markdown outside the registry claims a self-spec rule count. A second doc growing
    ///     the same sentence is the way this drift returns, and a hand-written registry cannot catch that
    ///     itself.
    /// </summary>
    [Fact]
    public void EveryTrackedDocClaimingARuleCount_IsRegistered()
    {
        Gate.ShouldFindNothingOutsideTheRegistry(
            counted: "self-spec rule count(s)",
            swept: "self-spec rule counts",
            authority: "the rendered board");
    }
}
