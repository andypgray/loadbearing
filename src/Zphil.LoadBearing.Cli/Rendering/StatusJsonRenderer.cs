using System.Text.Json;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Roslyn.Diagnostics;

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
    /// <summary>
    ///     Writes the burndown document as the run's stdout line.
    ///     <paramref name="workspaceDiagnostics" /> is the rendered stream the caller composed and
    ///     <paramref name="diagnostics" /> the load's own verdict, whose project lists become the trust stamps.
    /// </summary>
    public static void Render(
        TextWriter output,
        CheckReport report,
        string solutionDirectory,
        string solutionName,
        string specAssembly,
        IReadOnlyList<string> workspaceDiagnostics,
        WorkspaceDiagnostics diagnostics)
    {
        WorkspaceTrustStamp trust = WorkspaceTrustStamp.From(diagnostics, solutionDirectory);

        var document = new StatusJson(
            2,
            solutionName,
            specAssembly,
            report.Results.Select(ToRule).ToList(),
            workspaceDiagnostics.Count > 0 ? workspaceDiagnostics : null,
            trust.ModelIncomplete,
            trust.FailedProjects,
            trust.UncheckedProjects,
            trust.RestoreFailedProjects,
            trust.UnsupportedProjects,
            new StatusSummaryJson(
                report.RulesChecked,
                report.RulesPassed,
                report.RulesFailed,
                report.RulesSkipped,
                report.GrandfatheredCount,
                report.GrandfatheredSiteCount,
                report.StaleBaselineEntryCount,
                LoadBearingJson.OmitZero(report.ShrunkBaselineEntryCount),
                LoadBearingJson.OmitZero(report.UncountedBaselineEntryCount)));

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

    // The burndown block for any ratcheted rule (Migrate or Quarantine containment). Whether the ratchet has
    // burned to zero is RuleResult.Promotable's answer, one model fact the human line reads too; the wire
    // shape adds only that quarantine omits the key rather than carrying a permanent false, since
    // Quarantine→Migrate is a human decision this document has nothing to say about.
    private static RatchetStatusJson? ToRatchet(RuleResult result)
    {
        if (result.Rule.BaselinePath is not { } path) return null;

        bool? promotable = result.Rule.Posture == Posture.Migrate ? result.Promotable : null;
        return new RatchetStatusJson(
            path,
            result.BaselineCaptured,
            result.Grandfathered.Count,
            result.GrandfatheredSiteCount,
            result.Violations.Count,
            result.StaleBaselineEntries,
            LoadBearingJson.OmitZero(result.ShrunkBaselineEntries),
            LoadBearingJson.OmitZero(result.UncountedBaselineEntries),
            promotable);
    }
}
