using System.Text.Json;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn.Diagnostics;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     Renders a <see cref="CheckReport" /> as the <c>--json</c> document (schemaVersion 3) — the only
///     content on stdout in JSON mode, so hooks can parse it.
/// </summary>
/// <remarks>
///     <para>
///         The optional <c>diffBase</c> echoes the <c>--diff-base</c> ref and <c>rulesFilter</c> the
///         <c>--rules</c> globs (each omitted when absent), so a reader can tell a narrowed report from a
///         whole one. Machine-independent: <c>solution</c> and <c>specAssembly</c> are file names, and every
///         site path is solution-relative with forward slashes. Serialization lives here so Core stays
///         dependency-free; the options are the shared <see cref="LoadBearingJson.Options" />.
///     </para>
///     <para>
///         The document is composed as a string rather than written straight out, so a caller with a
///         response budget can measure the report and, if it overruns, re-compose it one grain coarser from
///         the same result model — one check, two renders, and never a document cut mid-array.
///     </para>
/// </remarks>
internal static class JsonReportRenderer
{
    /// <summary>
    ///     The report document as a string. <paramref name="grain" /> decides how much of each rule is
    ///     rendered and stamps itself on the document; the counts in <c>summary</c> are of what the run
    ///     evaluated and never move with it, so a coarser report still totals the same solution.
    ///     <paramref name="workspaceDiagnostics" /> is the rendered stream the caller composed (with or
    ///     without merge notes) and <paramref name="diagnostics" /> the load's own verdict, whose project
    ///     lists become the trust stamps.
    /// </summary>
    public static string Document(
        CheckReport report,
        string solutionDirectory,
        string solutionName,
        string specAssembly,
        string? diffBase,
        IReadOnlyList<string> workspaceDiagnostics,
        WorkspaceDiagnostics diagnostics,
        IReadOnlyList<string> rulesFilter,
        DocumentGrain grain)
    {
        // One relativizer for the whole document: the base directory is the same string for every site,
        // and normalizing plus splitting it is the constant half of the walk.
        var relativizer = new PathFormat.Relativizer(solutionDirectory);
        WorkspaceTrustStamp trust = WorkspaceTrustStamp.From(diagnostics, relativizer);

        // Every argument is named, which is what holds the six trust slots to the record's declaration
        // order for a reader; the key order is the DTO's to state.
        var document = new CheckJson(
            SchemaVersion: 3,
            Solution: solutionName,
            SpecAssembly: specAssembly,
            Grain: DocumentGrains.Wire(grain),
            DiffBase: diffBase,
            RulesFilter: rulesFilter.Count > 0 ? rulesFilter : null,
            ModelIncomplete: trust.ModelIncomplete,
            FailedProjects: trust.FailedProjects,
            UncheckedProjects: trust.UncheckedProjects,
            RestoreFailedProjects: trust.RestoreFailedProjects,
            UnsupportedProjects: trust.UnsupportedProjects,
            MultiTargetedProjects: trust.MultiTargetedProjects,
            Summary: new SummaryJson(
                report.RulesChecked,
                report.RulesPassed,
                report.RulesFailed,
                report.RulesSkipped,
                report.ViolationCount,
                report.WarningCount),
            Rules: report.Results.Select(r => ToRule(r, relativizer, grain)).ToList(),
            WorkspaceDiagnostics: workspaceDiagnostics);

        return JsonSerializer.Serialize(document, LoadBearingJson.Context.CheckJson);
    }

    // Everything above the violations survives every rung: the id, the verdict, the prose, the baseline and
    // the warnings. That is deliberate and is what keeps the coarsest report actionable — prose scales with
    // the rule count, which is authored and small, while violations and their sites scale with the codebase,
    // which is what actually overruns a channel. Dropping because/fix would leave an id and a number.
    private static RuleJson ToRule(RuleResult result, PathFormat.Relativizer relativizer, DocumentGrain grain)
    {
        bool elideViolations = grain >= DocumentGrain.Skeleton;

        return new RuleJson(
            result.Rule.Id,
            result.Rule.Posture,
            result.Status,
            result.Rule.Sentence,
            result.Rule.Because,
            result.Rule.Fix,
            result.SkipReason,
            ToBaseline(result),
            // Both or neither: the denominator alone says nothing worth a key, and the numerator alone is
            // unreadable. Non-zero is the whole gate, which is what makes the pair vanish the moment a rule
            // is narrowed to authored types.
            result.SubjectGeneratedTypes > 0 ? result.SubjectTypes : null,
            result.SubjectGeneratedTypes > 0 ? result.SubjectGeneratedTypes : null,
            elideViolations
                ? null
                : result.Violations.Select(v => ToViolation(v, relativizer, grain)).ToList(),
            elideViolations ? result.Violations.Count : null,
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
    private static ViolationJson ToViolation(
        Violation violation, PathFormat.Relativizer relativizer, DocumentGrain grain)
    {
        // The first and largest elision: sites are the one array with no ceiling — a legacy migration
        // burndown carries thousands of them under one rule — so this is the rung that actually compresses.
        // Eliding them skips the relativizer walk too, which is why a coarser render is cheaper as well as
        // smaller.
        bool elideSites = grain >= DocumentGrain.Overview;

        return new ViolationJson(
            violation.Kind,
            violation.Source?.FullName,
            violation.Target?.FullName,
            violation.Member?.SymbolId,
            violation.Subject?.FullName,
            violation.SubjectMember?.SymbolId,
            violation.Detail,
            elideSites
                ? null
                : violation.Sites.Select(s => new SiteJson(relativizer.Relative(s.FilePath), s.Line)).ToList(),
            elideSites ? violation.Sites.Count : null);
    }
}
