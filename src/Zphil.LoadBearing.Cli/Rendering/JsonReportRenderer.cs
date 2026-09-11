using System.Text.Json;
using Zphil.LoadBearing.Baselines;
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
///         the same result model — one check, one render per rung walked, and never a document cut
///         mid-array.
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

        // The floor rung elides MSBuild's own words, which is the one array here that scales with neither
        // the spec nor even the codebase but with the load's troubles — one entry per project per framework
        // per complaint, and on a bed whose package audit feed was unreachable it was 96% of this document
        // at every grain. Its actionable half is already keyed above, so what goes is the raw text.
        bool elideDiagnostics = grain >= DocumentGrain.Index && workspaceDiagnostics.Count > 0;

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
            WorkspaceDiagnostics: elideDiagnostics ? null : workspaceDiagnostics,
            WorkspaceDiagnosticCount: elideDiagnostics ? workspaceDiagnostics.Count : null);

        return JsonSerializer.Serialize(document, LoadBearingJson.Context.CheckJson);
    }

    // Two rungs act on a rule, and they answer different questions. Skeleton keeps the prose, because that is
    // what makes it a verdict a reader can act on without a second call: prose scales with the rule count,
    // which is authored and small, while violations and their sites scale with the codebase, which is what
    // actually overruns a channel. Index drops it, and is not competing with skeleton for that reader — it
    // competes with a cut document, which is corrupt JSON. What survives is the id and the verdict, which is
    // exactly the menu the next call needs: rules globs match these ids, and arch_explain takes one and
    // returns that rule whole, prose and all.
    private static RuleJson ToRule(RuleResult result, PathFormat.Relativizer relativizer, DocumentGrain grain)
    {
        bool elideViolations = grain >= DocumentGrain.Skeleton;
        bool elideProse = grain >= DocumentGrain.Index;

        return new RuleJson(
            result.Rule.Id,
            result.Rule.Posture,
            result.Status,
            elideProse ? null : result.Rule.Sentence,
            elideProse ? null : result.Rule.Because,
            elideProse ? null : result.Rule.Fix,
            elideProse ? null : result.Rule.Citation,
            result.SkipReason,
            ToBaseline(result),
            // Both or neither: the denominator alone says nothing worth a key, and the numerator alone is
            // unreadable. Non-zero is the whole gate, which is what makes the pair vanish the moment a rule
            // is narrowed to authored types.
            result.SubjectGeneratedTypes > 0 ? result.SubjectTypes : null,
            result.SubjectGeneratedTypes > 0 ? result.SubjectGeneratedTypes : null,
            result.Violations.Count,
            elideViolations
                ? null
                : result.Violations.Select(v => ToViolation(v, result, relativizer, grain)).ToList(),
            result.Warnings.Select(w => new WarningJson(w.Kind, w.Message)).ToList());
    }

    // The baseline block is present for any ratcheted rule (Migrate or Quarantine containment); the model's
    // relative path string rides through verbatim. The two measure counts are omitted at zero rather than
    // written as 0, so a section whose entries all record a count they still hold reads exactly as it did
    // before the measure existed.
    private static BaselineJson? ToBaseline(RuleResult result)
    {
        return result.Rule.BaselinePath is { } path
            ? new BaselineJson(
                path,
                result.Grandfathered.Count,
                result.StaleBaselineEntries,
                LoadBearingJson.OmitZero(result.ShrunkBaselineEntries),
                LoadBearingJson.OmitZero(result.UncountedBaselineEntries))
            : null;
    }

    // A memberUse violation carries Source (the using type, as Reference does) and the banned member's raw
    // symbol ID in targetMember; a memberShape violation carries the offending member's raw symbol ID in
    // subjectMember (Subject/Target stay null); a projectShape violation carries the project's name in
    // subjectProject and, when it is one of the per-package ones, the package's name beside it. Every slot
    // is null-omitted, so a report from a spec with no member or project rule carries none of those keys.
    // The rule comes in whole for the one slot that is a fact about this violation's relationship to the
    // baseline rather than about the violation itself: what its entry allowed, when it is red for exceeding it.
    private static ViolationJson ToViolation(
        Violation violation, RuleResult result, PathFormat.Relativizer relativizer, DocumentGrain grain)
    {
        // The first and largest elision: sites are the one array with no ceiling — a legacy migration
        // burndown carries thousands of them under one rule — so this is the rung that actually compresses.
        // Eliding them skips the relativizer walk too, which is why a coarser render is cheaper as well as
        // smaller.
        bool elideSites = grain >= DocumentGrain.Overview;
        int? grandfatheredSiteCount = result.GrownEntries.TryGetValue(violation, out BaselineEntry? grown)
            ? grown.SiteCount
            : null;

        return new ViolationJson(
            violation.Kind,
            violation.Source?.FullName,
            violation.Target?.FullName,
            violation.Member?.SymbolId,
            violation.Subject?.FullName,
            violation.SubjectMember?.SymbolId,
            violation.SubjectProject?.Name,
            violation.Package?.Name,
            violation.Detail,
            grandfatheredSiteCount,
            violation.Sites.Count,
            elideSites
                ? null
                : violation.Sites.Select(s => new SiteJson(relativizer.Relative(s.FilePath), s.Line)).ToList());
    }
}
