using Zphil.LoadBearing.Checking;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     Formats a <see cref="CheckReport" /> as the human <c>status</c> report — one line per rule plus a
///     burndown summary. Pure over the report (no workspace), so the line shapes are unit-pinned. Enforce
///     rules read <c>pass</c>/<c>FAIL</c> with violation/warning counts; a ratcheted rule (Migrate or
///     Freeze containment) reads the ratchet state — grandfathered remaining, new (red), and
///     fixed-awaiting-acceptance; a Freeze tripwire reads <c>skip</c> (diff-aware). Only Migrate surfaces
///     the promotion suggestion when the baseline has burned to zero — Freeze→Migrate is a human decision.
/// </summary>
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
        return result.Rule.Posture switch
        {
            Posture.Migrate => RatchetLine(result, "migrate"),
            Posture.Freeze => FreezeLine(result),
            _ => EnforceLine(result)
        };
    }

    private static string FreezeLine(RuleResult result)
    {
        // Containment ratchets like Migrate (but never suggests promotion); the tripwire is diff-aware skip.
        return result.Rule.Freeze!.Role == FreezeRole.Containment
            ? RatchetLine(result, "freeze")
            : $"skip {result.Rule.Id} (tripwire) — diff-aware; run 'loadbearing check --diff-base <ref>'";
    }

    private static string EnforceLine(RuleResult result)
    {
        var details = new List<string>();
        if (result.Violations.Count > 0) details.Add($"{result.Violations.Count} {Plural(result.Violations.Count, "violation")}");
        if (result.Warnings.Count > 0) details.Add($"{result.Warnings.Count} {Plural(result.Warnings.Count, "warning")}");

        string marker = result.Status == RuleStatus.Failed ? "FAIL" : "pass";
        return details.Count > 0 ? $"{marker} {result.Rule.Id} — {string.Join(", ", details)}" : $"{marker} {result.Rule.Id}";
    }

    // Shared by Migrate and Freeze containment. The promotable branch fires only for Migrate — freeze
    // promotion (Freeze→Migrate) is a human decision, so a burned-to-zero containment reads plain.
    private static string RatchetLine(RuleResult result, string postureLabel)
    {
        int remaining = result.Grandfathered.Count;
        int newCount = result.Violations.Count;
        int stale = result.StaleBaselineEntries;
        string marker = result.Status == RuleStatus.Failed ? "FAIL" : "pass";
        bool promotable = postureLabel == "migrate";
        return $"{marker} {result.Rule.Id} ({postureLabel}) — {RatchetDetail(result.BaselineCaptured, remaining, newCount, stale, promotable)}";
    }

    private static string RatchetDetail(bool captured, int remaining, int newCount, int stale, bool promotable)
    {
        if (!captured)
            return $"no baseline captured; run 'loadbearing baseline --init' ({newCount} current {Plural(newCount, "violation")})";
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

    private static string Plural(int count, string noun)
    {
        return count == 1 ? noun : noun + "s";
    }
}