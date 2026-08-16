using System.Text.Json;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Rendering;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     Renders a <see cref="CheckReport" /> as the <c>status --json</c> document (its own schemaVersion 2)
///     — burndown counts per ratcheted rule (Migrate and Quarantine containment) plus, for Migrate, the
///     promotion flag (omitted for quarantine).
/// </summary>
/// <remarks>
///     Uses the shared <see cref="LoadBearingJson.Options" />; machine-independent
///     (<c>solution</c>/<c>specAssembly</c> are file names).
/// </remarks>
internal static class StatusJsonRenderer
{
    public static void Render(
        TextWriter output,
        CheckReport report,
        string solutionDirectory,
        string solutionName,
        string specAssembly,
        IReadOnlyList<string> workspaceDiagnostics,
        bool modelIncomplete,
        IReadOnlyList<string> failedProjects,
        IReadOnlyList<string> uncheckedProjects)
    {
        var relativizer = new PathFormat.Relativizer(solutionDirectory);

        var document = new StatusJson(
            2,
            solutionName,
            specAssembly,
            report.Results.Select(ToRule).ToList(),
            workspaceDiagnostics.Count > 0 ? workspaceDiagnostics : null,
            modelIncomplete ? true : null,
            JsonReportRenderer.RelativeProjects(failedProjects, relativizer),
            JsonReportRenderer.RelativeProjects(uncheckedProjects, relativizer),
            new StatusSummaryJson(
                report.RulesChecked,
                report.RulesPassed,
                report.RulesFailed,
                report.RulesSkipped,
                report.GrandfatheredCount,
                report.StaleBaselineEntryCount));

        output.WriteLine(JsonSerializer.Serialize(document, LoadBearingJson.Context.StatusJson));
    }

    private static StatusRuleJson ToRule(RuleResult result)
    {
        return new StatusRuleJson(
            result.Rule.Id,
            result.Rule.Posture,
            result.Status,
            result.Violations.Count,
            result.Warnings.Count,
            ToRatchet(result));
    }

    // The burndown block for any ratcheted rule (Migrate or Quarantine containment). Promotable is populated
    // for Migrate only — omitted (null) for quarantine, since Quarantine→Migrate is a human decision — and
    // never for a rule the run reached no verdict on: a narrowing skip keeps BaselineCaptured truthful and
    // zeroes the counts, which is burned-to-zero's exact shape, so arch_status would suggest promoting to
    // Enforce a rule whose subject a filter had merely erased.
    private static RatchetStatusJson? ToRatchet(RuleResult result)
    {
        if (result.Rule.BaselinePath is not { } path) return null;

        bool? promotable = result.Rule.Posture == Posture.Migrate
            ? result.Status != RuleStatus.Skipped
              && result.BaselineCaptured
              && result.Grandfathered.Count == 0
              && result.Violations.Count == 0
              && result.StaleBaselineEntries == 0
            : null;
        return new RatchetStatusJson(
            path,
            result.BaselineCaptured,
            result.Grandfathered.Count,
            result.Violations.Count,
            result.StaleBaselineEntries,
            promotable);
    }
}
