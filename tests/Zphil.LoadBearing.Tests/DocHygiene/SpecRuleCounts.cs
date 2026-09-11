using System.Text.RegularExpressions;
using Zphil.LoadBearing.Rendering;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     The checker the self-spec rule-count gate runs on. Prose that says how many rules govern this
///     repository is a hand-maintained number with a generated answer sitting beside it, so this lifts
///     every such claim out of a doc's committed bytes and counts the rule bullets the committed
///     <c>AGENTS.md</c> managed block actually renders. All of the logic lives here so the gate and the
///     unit tests exercise the same code path.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why this gate exists.</b> The root README said <c>Eighteen rules</c> from 2026-08-01 until
///         0.6.0, across four published releases, while the spec grew 20 → 27 authored statements. It was
///         wrong within days of being written. Every other number in that README is held to something —
///         grandfathered counts to their baselines, quoted sentences to the rendered board, anchors to
///         their committed line — and the rule count was the one claim with nothing behind it. Worse, it
///         sits in the sentence that promises the page is spec-derived, so the claim that the page cannot
///         rot was itself the rotting part.
///     </para>
///     <para>
///         <b>The board is the authority, not the spec source.</b> Counting <c>arch.Rule(</c> call sites
///         would miss the two the <c>DotNetGuidance</c> pack contributes and would count a
///         <c>arch.Scope(</c> as nothing, so it answers 32 where a reader counting the rendered board
///         answers 35. The managed block is what a reader actually sees, it already carries one bullet per
///         declared rule across all three posture sections, and the dogfood self-spec tests hold it to
///         what the spec emits — so holding the prose to the board chains it to the spec through a gate
///         that already exists.
///     </para>
///     <para>
///         <b>Not the same number as <c>check</c>'s, nor as the spec file's.</b> A check report says more
///         than the board because a quarantined scope desugars into its containment and tripwire children
///         and a cautioned scope into a tripwire alone — rules the spec never spells — and the spec file
///         holds one statement more than the board because a caution is a statement that is not a rule
///         over real code: nothing about it can fail, so it has no bullet on the board and no place in a
///         count of rules. The board's count is the count a claim about what an author wrote should
///         state. A doc quoting a <c>Checked N rules</c> summary is
///         holding a different fact against a different source, and <see cref="ProseQuotedOutputTests" />
///         is where that one lives.
///     </para>
///     <para>
///         <b>Committed bytes only.</b> This reads the generated file git tracks, never the renderer, so
///         the gate needs no workspace and stays cheap, cross-platform and parallel-safe. That the
///         committed <c>AGENTS.md</c> still matches what the spec emits is proved by the dogfood
///         self-spec tests and by CI re-rendering and failing on any diff.
///     </para>
/// </remarks>
internal static class SpecRuleCounts
{
    /// <summary>
    ///     A rule bullet in a rendered managed block: <c>- `layering/core-no-roslyn` — …</c>. The
    ///     backticked id must carry at least one <c>/</c>, which is what keeps the layer bullets beside
    ///     them (<c>- **Core** — …</c>) out of the count without naming the sections to look in.
    /// </summary>
    private static readonly Regex RuleBullet = new(
        @"^- `[a-z0-9]+(-[a-z0-9]+)*(/[a-z0-9]+(-[a-z0-9]+)*)+` — ",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>
    ///     A prose claim about how many rules govern this repository. The number is captured as written —
    ///     a spelled word or a numeral — so the gate compares what the doc says rather than what a parse
    ///     of it rounded to.
    /// </summary>
    private static readonly Regex ClaimPattern = new(
        @"\b(?<count>[A-Za-z][a-z]*(-[a-z]+)?|\d+) rules over this repository's real code\b",
        RegexOptions.CultureInvariant);

    /// <summary>
    ///     Every rule bullet the managed block in <paramref name="agentsMarkdown" /> renders, across all
    ///     three posture sections. Reads the block body alone, so prose above or below the markers cannot
    ///     inflate it.
    /// </summary>
    public static int RenderedRuleCount(string agentsMarkdown)
    {
        string? body = ManagedBlock.ExtractBody(agentsMarkdown);
        if (body is null) return 0;

        return RuleBullet.Matches(body)
            .Count;
    }

    /// <summary>
    ///     Every self-spec rule-count claim in <paramref name="docText" />, named by
    ///     <paramref name="doc" />'s repo-relative path.
    /// </summary>
    public static IReadOnlyList<SpecRuleCountClaim> Extract(string doc, string docText)
    {
        List<SpecRuleCountClaim> claims = new();
        string[] lines = docText.Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            foreach (Match match in ClaimPattern.Matches(line))
            {
                string written = match.Groups["count"]
                    .Value;

                // A delta reads "Four new rules over this repository's real code", so the token against
                // "rules" is the qualifier rather than the number, and the claim is a fact about one past
                // release rather than about the spec today. Requiring a number there is what tells a total
                // from a delta, and it is why the CHANGELOG needs no exemption entry.
                if (!GrandfatheredCounts.IsCountWord(written)) continue;

                claims.Add(new SpecRuleCountClaim(doc, index + 1, written));
            }
        }

        return claims;
    }
}
