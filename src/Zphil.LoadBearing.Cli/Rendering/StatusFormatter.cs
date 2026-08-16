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
///     fixed-awaiting-acceptance; a Quarantine tripwire reads <c>skip</c> (diff-aware). Only Migrate surfaces
///     the promotion suggestion when the baseline has burned to zero — Quarantine→Migrate is a human decision.
///     A rule a solution filter left no subject for reads <c>skip</c> with its reason, whatever its posture:
///     the burndown its posture would otherwise print is a claim about a check this run never made.
/// </remarks>
internal static class StatusFormatter
{
    public static IReadOnlyList<string> Lines(CheckReport report)
    {
        var lines = report.Results.Select(RuleLine).ToList();
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
        int remaining = result.Grandfathered.Count;
        int newCount = result.Violations.Count;
        int stale = result.StaleBaselineEntries;
        string marker = result.Status == RuleStatus.Failed ? "FAIL" : "pass";
        bool promotable = result.Promotable;
        return $"{marker} {result.Rule.Id} ({postureLabel}) — {RatchetDetail(result.BaselineCaptured, remaining, newCount, stale, promotable)}";
    }

    private static string RatchetDetail(bool captured, int remaining, int newCount, int stale, bool promotable)
    {
        if (!captured)
            return $"no baseline captured; run 'loadbearing baseline --init' ({newCount} current {Plurals.Noun(newCount, "violation")})";
        if (newCount > 0)
            return $"{remaining} grandfathered remaining, {newCount} new, {stale} fixed awaiting acceptance";
        if (remaining == 0 && stale == 0)
            return promotable ? "0 remaining; promotable to Enforce (baseline is empty)" : "0 grandfathered remaining";
        if (remaining == 0)
            return $"0 remaining, {stale} fixed awaiting acceptance; run 'loadbearing baseline --accept-reductions'";
        return $"{remaining} grandfathered remaining, 0 new, {stale} fixed awaiting acceptance";
    }

    private static string Summary(CheckReport report)
    {
        return $"Checked {report.RulesChecked} rules: {report.RulesPassed} passed, {report.RulesFailed} failed, " +
               $"{report.RulesSkipped} skipped. Burndown: {report.GrandfatheredCount} grandfathered remaining, " +
               $"{report.StaleBaselineEntryCount} fixed awaiting acceptance.";
    }
}
