using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Rendering;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     Formats the <c>baseline</c> ratchet survey's verdict on the rules the ratchet cannot reach: the
///     rules failing with no baseline to capture (Enforce — Migrate and Quarantine containment always
///     carry a baseline path).
/// </summary>
/// <remarks>
///     Pure over the report (no workspace), so the line shapes are unit-pinned.
///     With no ratcheted rule in the spec it owns the whole survey line — "nothing to do." when nothing
///     is failing, "nothing to capture." plus the notice when something is; beside ratchet work it
///     appends the notice alone, and it stays silent when every failure is ratcheted.
/// </remarks>
internal static class RatchetSurveyNotice
{
    public static IReadOnlyList<string> Lines(CheckReport report, bool anyRatchetedRule)
    {
        var unratcheted = report.Results
            .Where(r => r.Status == RuleStatus.Failed && r.Rule.BaselinePath is null)
            .ToList();

        var lines = new List<string>();
        if (!anyRatchetedRule)
            lines.Add(unratcheted.Count == 0
                ? "no ratcheted rules (Migrate or Quarantine containment) in the spec; nothing to do."
                : "no ratcheted rules (Migrate or Quarantine containment) in the spec; nothing to capture.");

        if (unratcheted.Count == 0) return lines;

        int violationCount = unratcheted.Sum(r => r.Violations.Count);
        string ruleVerb = Plurals.Verb(unratcheted.Count);
        lines.Add(
            $"{unratcheted.Count} {Plurals.Noun(unratcheted.Count, "rule")} {ruleVerb} failing with no baseline to capture " +
            $"({violationCount} {Plurals.Noun(violationCount, "violation")}):");
        foreach (RuleResult result in unratcheted)
            lines.Add($"  {result.Rule.Id} — {result.Violations.Count} {Plurals.Noun(result.Violations.Count, "violation")}");
        lines.Add(
            "Enforce carries no baseline, so 'check' stays red on these until each is fixed at the source — " +
            "in the code, in the rule, or by re-posturing the debt as Migrate or Quarantine.");
        return lines;
    }
}
