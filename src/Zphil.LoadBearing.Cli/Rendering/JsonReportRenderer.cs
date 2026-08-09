using System.Text.Json;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Rendering;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     Renders a <see cref="CheckReport" /> as the <c>--json</c> document (schemaVersion 3) — the only
///     content on stdout in JSON mode, so hooks can parse it.
/// </summary>
/// <remarks>
///     The optional <c>diffBase</c> echoes the <c>--diff-base</c> ref and <c>rulesFilter</c> the
///     <c>--rules</c> globs (each omitted when absent), so a reader can tell a narrowed report from a
///     whole one. Machine-independent: <c>solution</c> and <c>specAssembly</c> are file names, and every
///     site path is solution-relative with forward slashes. Serialization lives here so Core stays
///     dependency-free; the options are the shared <see cref="LoadBearingJson.Options" />.
/// </remarks>
internal static class JsonReportRenderer
{
    public static void Render(
        TextWriter output,
        CheckReport report,
        string solutionDirectory,
        string solutionName,
        string specAssembly,
        string? diffBase,
        IReadOnlyList<string> workspaceDiagnostics,
        bool modelIncomplete,
        IReadOnlyList<string> rulesFilter)
    {
        // One relativizer for the whole document: the base directory is the same string for every site,
        // and normalizing plus splitting it is the constant half of the walk.
        var relativizer = new PathFormat.Relativizer(solutionDirectory);

        var document = new CheckJson(
            3,
            solutionName,
            specAssembly,
            diffBase,
            rulesFilter.Count > 0 ? rulesFilter : null,
            report.Results.Select(r => ToRule(r, relativizer)).ToList(),
            workspaceDiagnostics,
            modelIncomplete ? true : null,
            new SummaryJson(
                report.RulesChecked,
                report.RulesPassed,
                report.RulesFailed,
                report.RulesSkipped,
                report.ViolationCount,
                report.WarningCount));

        output.WriteLine(JsonSerializer.Serialize(document, LoadBearingJson.Context.CheckJson));
    }

    private static RuleJson ToRule(RuleResult result, PathFormat.Relativizer relativizer)
    {
        return new RuleJson(
            result.Rule.Id,
            result.Rule.Posture,
            result.Status,
            result.Rule.Sentence,
            result.Rule.Because,
            result.Rule.Fix,
            result.SkipReason,
            ToBaseline(result),
            result.Violations.Select(v => ToViolation(v, relativizer)).ToList(),
            result.Warnings.Select(w => new WarningJson(w.Kind, w.Message)).ToList());
    }

    // The baseline block is present for any ratcheted rule (Migrate or Quarantine containment); the model's
    // relative path string rides through verbatim.
    private static BaselineJson? ToBaseline(RuleResult result)
    {
        return result.Rule.BaselinePath is { } path
            ? new BaselineJson(path, result.Grandfathered.Count, result.StaleBaselineEntries)
            : null;
    }

    // A memberUse violation carries Source (the using type, as Reference does) and the banned member's raw
    // symbol ID in targetMember; a memberShape violation carries the offending member's raw symbol ID in
    // subjectMember (Subject/Target stay null). Every slot is null-omitted, so a report from a spec with
    // no member rule carries neither key.
    private static ViolationJson ToViolation(Violation violation, PathFormat.Relativizer relativizer)
    {
        return new ViolationJson(
            violation.Kind,
            violation.Source?.FullName,
            violation.Target?.FullName,
            violation.Member?.SymbolId,
            violation.Subject?.FullName,
            violation.SubjectMember?.SymbolId,
            violation.Detail,
            violation.Sites.Select(s => new SiteJson(relativizer.Relative(s.FilePath), s.Line)).ToList());
    }
}
