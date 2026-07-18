using System.Text.Json;
using Zphil.LoadBearing.Checking;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     Renders a <see cref="CheckReport" /> as the <c>status --json</c> document (its own schemaVersion 2)
///     — burndown counts per ratcheted rule (Migrate and Freeze containment) plus, for Migrate, the
///     promotion flag (omitted for freeze). Uses the shared <see cref="LoadBearingJson.Options" />;
///     machine-independent (<c>solution</c>/<c>specAssembly</c> are file names).
/// </summary>
internal static class StatusJsonRenderer
{
    public static void Render(TextWriter output, CheckReport report, string solutionName, string specAssembly)
    {
        var document = new StatusJson(
            2,
            solutionName,
            specAssembly,
            report.Results.Select(ToRule).ToList(),
            new StatusSummaryJson(
                report.RulesChecked, report.RulesPassed, report.RulesFailed, report.RulesSkipped,
                report.GrandfatheredCount, report.StaleBaselineEntryCount));

        output.WriteLine(JsonSerializer.Serialize(document, LoadBearingJson.Options));
    }

    private static StatusRuleJson ToRule(RuleResult result)
    {
        return new StatusRuleJson(
            result.Rule.Id,
            Camel(result.Rule.Posture.ToString()),
            Camel(result.Status.ToString()),
            result.Violations.Count,
            result.Warnings.Count,
            ToRatchet(result));
    }

    // The burndown block for any ratcheted rule (Migrate or Freeze containment). Promotable is populated
    // for Migrate only — omitted (null) for freeze, since Freeze→Migrate is a human decision.
    private static RatchetStatusJson? ToRatchet(RuleResult result)
    {
        if (result.Rule.BaselinePath is not { } path) return null;

        bool? promotable = result.Rule.Posture == Posture.Migrate
            ? result.BaselineCaptured
              && result.Grandfathered.Count == 0
              && result.Violations.Count == 0
              && result.StaleBaselineEntries == 0
            : null;
        return new RatchetStatusJson(
            path, result.BaselineCaptured, result.Grandfathered.Count, result.Violations.Count,
            result.StaleBaselineEntries, promotable);
    }

    private static string Camel(string name)
    {
        return name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);
    }
}