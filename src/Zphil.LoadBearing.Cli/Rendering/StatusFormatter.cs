using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Rendering;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     Formats a <see cref="CheckReport" /> as the human <c>status</c> report — one line per rule plus a
///     burndown summary.
/// </summary>
/// <remarks>
///     Pure over the report (no workspace), so the line shapes are unit-pinned. Enforce
///     rules read <c>pass</c>/<c>FAIL</c> with violation/warning counts; a ratcheted rule (Migrate or
///     Quarantine containment) reads the ratchet state — grandfathered remaining, new (red), and
///     fixed-awaiting-acceptance, plus the three measure terms where they apply: the site total behind the
///     remaining pairs, and how many matched entries came in under their recorded count or record none at
///     all. Each of those three extinguishes itself, so a rule whose pairs are one site apiece and whose
///     counts all hold reads exactly as it did before the measure existed. A Quarantine tripwire reads
///     <c>skip</c> (diff-aware). Only Migrate surfaces
///     the promotion suggestion when the baseline has burned to zero — Quarantine→Migrate is a human decision.
///     A rule a solution filter left no subject for reads <c>skip</c> with its reason, whatever its posture:
///     the burndown its posture would otherwise print is a claim about a check this run never made.
/// </remarks>
internal static class StatusFormatter
{
    public static IReadOnlyList<string> Lines(CheckReport report)
    {
        List<string> lines = report.Results.Select(RuleLine).ToList();
        lines.Add(Summary(report));
        return lines;
    }

    private static string RuleLine(RuleResult result)
    {
        // Ahead of the posture dispatch, because posture is what would mislead: a narrowing-skipped Migrate
        // rule falls to RatchetLine and reads "pass … 0 new, 2 fixed awaiting acceptance" — a pass and a
        // burndown for a rule the run never measured. The tripwire is Skipped for its own reason and keeps
        // its own line, which says what a diff-aware skip is and what to run to get a verdict.
        if (result.Status == RuleStatus.Skipped && !IsTripwire(result))
            return $"skip {result.Rule.Id} — {result.SkipReason}";

        return result.Rule.Posture switch
        {
            Posture.Migrate => RatchetLine(result, "migrate"),
            Posture.Quarantine => QuarantineLine(result),
            _ => EnforceLine(result)
        };
    }

    private static bool IsTripwire(RuleResult result)
    {
        return result.Rule.Quarantine is { Role: QuarantineRole.Tripwire };
    }

    private static string QuarantineLine(RuleResult result)
    {
        // Containment ratchets like Migrate (but never suggests promotion); the tripwire is diff-aware skip.
        return result.Rule.Quarantine!.Role == QuarantineRole.Containment
            ? RatchetLine(result, "quarantine")
            : $"skip {result.Rule.Id} (tripwire) — diff-aware; run 'loadbearing check --diff-base <ref>'";
    }

    private static string EnforceLine(RuleResult result)
    {
        var details = new List<string>();
        if (result.Violations.Count > 0) details.Add($"{result.Violations.Count} {Plurals.Noun(result.Violations.Count, "violation")}");
        if (result.Warnings.Count > 0) details.Add($"{result.Warnings.Count} {Plurals.Noun(result.Warnings.Count, "warning")}");

        string marker = result.Status == RuleStatus.Failed ? "FAIL" : "pass";
        return details.Count > 0 ? $"{marker} {result.Rule.Id} — {string.Join(", ", details)}" : $"{marker} {result.Rule.Id}";
    }

    // Shared by Migrate and Quarantine containment. The promotable branch fires only for Migrate — quarantine
    // promotion (Quarantine→Migrate) is a human decision, so a burned-to-zero containment reads plain. The
    // suggestion is the model's own RuleResult.Promotable rather than a second reading of the same counts,
    // so this line and the status document can never disagree about which rules are ready.
    private static string RatchetLine(RuleResult result, string postureLabel)
    {
        string marker = result.Status == RuleStatus.Failed ? "FAIL" : "pass";
        return $"{marker} {result.Rule.Id} ({postureLabel}) — {RatchetDetail(result)}";
    }

    // The whole result rather than its counts one by one: six ints in a row now feed this line, and a
    // transposed pair of them is a wrong burndown no compiler could have caught.
    private static string RatchetDetail(RuleResult result)
    {
        int remaining = result.Grandfathered.Count;
        int newCount = result.Violations.Count;
        int stale = result.StaleBaselineEntries;

        if (!result.BaselineCaptured)
            return $"no baseline captured; run 'loadbearing baseline --init' ({newCount} current {Plurals.Noun(newCount, "violation")})";

        // Both measures need a matched entry to be counted at all, so a rule with nothing remaining has
        // neither — which is why the two zero-remaining arms below say nothing about them. They trail the
        // stale term rather than joining the head of the list: like it, each names a state a write clears.
        int sites = result.Grandfathered.Sum(violation => violation.Sites.Count);
        string measures = Clause(result.ShrunkBaselineEntries, "shrunk")
                          + Clause(result.UncountedBaselineEntries, "uncounted");
        if (newCount > 0)
            return $"{remaining} grandfathered remaining{Sites(remaining, sites)}, {newCount} new, "
                   + $"{stale} fixed awaiting acceptance{measures}";
        if (remaining == 0 && stale == 0)
            return result.Promotable ? "0 remaining; promotable to Enforce (baseline is empty)" : "0 grandfathered remaining";
        if (remaining == 0)
            return $"0 remaining, {stale} fixed awaiting acceptance; run 'loadbearing baseline --accept-reductions'";
        return $"{remaining} grandfathered remaining{Sites(remaining, sites)}, 0 new, "
               + $"{stale} fixed awaiting acceptance{measures}";
    }

    // The site total, worth stating only where it says something the pair count does not. Strictly greater,
    // never merely different: a violation that carries no site at all (a rule error) would otherwise read
    // "(0 sites)", which claims a measurement nobody made.
    private static string Sites(int pairs, int sites)
    {
        return sites > pairs ? $" ({sites} sites)" : string.Empty;
    }

    // A measure clause that extinguishes itself at zero, so a fully counted rule whose counts all still hold
    // prints the line it printed before the measure existed.
    private static string Clause(int count, string term)
    {
        return count > 0 ? $", {count} {term}" : string.Empty;
    }

    // The roll-ups come off the report rather than being re-summed from the rules, so this line and the
    // burndown document can never disagree about the same run.
    private static string Summary(CheckReport report)
    {
        int remaining = report.GrandfatheredCount;
        int uncounted = report.UncountedBaselineEntryCount;

        // The nudge rides on the uncounted clause and nowhere else: it is advice, and once per run beneath
        // the burndown is where it is actionable rather than repeated down every rule line.
        string uncountedClause = uncounted > 0
            ? $", {uncounted} uncounted; run 'loadbearing baseline --accept-reductions' to record site counts"
            : string.Empty;

        return $"Checked {report.RulesChecked} rules: {report.RulesPassed} passed, {report.RulesFailed} failed, " +
               $"{report.RulesSkipped} skipped. Burndown: {remaining} grandfathered remaining" +
               $"{Sites(remaining, report.GrandfatheredSiteCount)}, " +
               $"{report.StaleBaselineEntryCount} fixed awaiting acceptance" +
               $"{Clause(report.ShrunkBaselineEntryCount, "shrunk")}{uncountedClause}.";
    }
}
