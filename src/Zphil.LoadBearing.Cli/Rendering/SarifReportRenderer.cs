using System.Text;
using System.Text.Json;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Cli.Mcp.Infrastructure;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn.Caching;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     Renders a <see cref="CheckReport" /> as a SARIF 2.1.0 file — a third render target over the same
///     result model (human and <c>--json</c> untouched) for GitHub/ADO code scanning and IDE problem
///     imports. One run: a driver whose <c>rules[]</c> is every rule in model order (metadata only), and
///     one result per violation <em>site</em> — a red violation as <c>error</c> / <c>baselineState: new</c>
///     with no suppression, a grandfathered violation as <c>note</c> / <c>baselineState: unchanged</c>
///     carrying an external suppression whose justification is the baseline entry's <c>because</c> (or a
///     generic <c>grandfathered in {path}</c> fallback). EmptySubject and RuleError violations are
///     site-less and so contribute no results (they still gate via the CLI exit code). Every path is
///     solution-relative against the <c>SRCROOT</c> URI base — no absolute path is ever emitted.
///     Serialization is the shared <see cref="LoadBearingJson.Options" />, so the SARIF golden and the JSON
///     golden cannot drift in escaping or casing. Pinned by the golden <c>Cli/Golden/violated-check.sarif</c>.
/// </summary>
internal static class SarifReportRenderer
{
    private const string SchemaUri = "https://json.schemastore.org/sarif-2.1.0.json";
    private const string SarifVersion = "2.1.0";
    private const string DriverName = "LoadBearing";
    private const string InformationUri = "https://github.com/andypgray/loadbearing";
    private const string SrcRootBaseId = "SRCROOT";
    private const string FingerprintKey = "loadBearingViolationIdentity/v1";
    private const string ErrorLevel = "error";
    private const string NoteLevel = "note";

    // UTF-8 without a BOM: SARIF consumers read UTF-8, and a leading BOM would churn the byte-level golden.
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>
    ///     Writes the SARIF file for <paramref name="report" /> to <paramref name="sarifPath" /> —
    ///     <see cref="Serialize" /> then an atomic, directory-creating write (UTF-8 no BOM, one trailing
    ///     newline). Throws on a bad path or I/O failure rather than swallowing, exactly like the
    ///     managed-block writer, so the top-level handler maps it to exit 2.
    /// </summary>
    public static void Render(
        string sarifPath,
        CheckReport report,
        string solutionDirectory,
        bool executionSuccessful,
        IReadOnlyList<string> workspaceDiagnostics)
    {
        string json = Serialize(report, solutionDirectory, executionSuccessful, workspaceDiagnostics);
        AtomicFile.WriteAllBytes(sarifPath, Utf8NoBom.GetBytes(json + "\n"));
    }

    /// <summary>
    ///     Serializes <paramref name="report" /> to the SARIF 2.1.0 JSON string — the workspace-free,
    ///     filesystem-free seam the unit tests drive directly. <paramref name="solutionDirectory" /> makes
    ///     every site path solution-relative; <paramref name="executionSuccessful" /> becomes the
    ///     invocation verdict (false when the incomplete-model gate will exit 2); and
    ///     <paramref name="workspaceDiagnostics" /> become tool-execution notifications (omitted when empty).
    /// </summary>
    internal static string Serialize(
        CheckReport report,
        string solutionDirectory,
        bool executionSuccessful,
        IReadOnlyList<string> workspaceDiagnostics)
    {
        var driver = new SarifDriver(DriverName, ServerVersion.SemVer, InformationUri, BuildRules(report));
        var run = new SarifRun(
            new SarifTool(driver),
            BuildInvocations(executionSuccessful, workspaceDiagnostics),
            BuildOriginalUriBaseIds(),
            BuildResults(report, solutionDirectory));
        var log = new SarifLog(SchemaUri, SarifVersion, new[] { run });
        return JsonSerializer.Serialize(log, LoadBearingJson.Options);
    }

    // Every rule, in model order — passed and skipped rules included (metadata carries the whole spec, not
    // just what failed). shortDescription is the law Sentence, omitted when empty (a Freeze tripwire);
    // fullDescription is the Because; help is the Fix, omitted when absent.
    private static IReadOnlyList<SarifReportingDescriptor> BuildRules(CheckReport report)
    {
        return report.Results
            .Select(result => result.Rule)
            .Select(rule => new SarifReportingDescriptor(
                rule.Id,
                rule.Sentence.Length > 0 ? new SarifMessage(rule.Sentence) : null,
                new SarifMessage(rule.Because),
                rule.Fix is { } fix ? new SarifMessage(fix) : null,
                new SarifReportingConfiguration(ErrorLevel),
                new SarifRuleProperties(Camel(rule.Posture.ToString()))))
            .ToList();
    }

    // Exactly one invocation. executionSuccessful is false when the workspace-diagnostics gate will exit 2;
    // the diagnostics themselves ride as warning-level notifications, the block omitted when there are none.
    private static IReadOnlyList<SarifInvocation> BuildInvocations(
        bool executionSuccessful, IReadOnlyList<string> workspaceDiagnostics)
    {
        IReadOnlyList<SarifNotification>? notifications = workspaceDiagnostics.Count > 0
            ? workspaceDiagnostics.Select(d => new SarifNotification(new SarifMessage(d), "warning")).ToList()
            : null;
        return new[] { new SarifInvocation(executionSuccessful, notifications) };
    }

    // {"SRCROOT": {}} — the one solution-root URI base every artifact location resolves against, so no
    // absolute path is ever written.
    private static IReadOnlyDictionary<string, SarifArtifactLocationBase> BuildOriginalUriBaseIds()
    {
        return new Dictionary<string, SarifArtifactLocationBase>(StringComparer.Ordinal)
        {
            [SrcRootBaseId] = new()
        };
    }

    // Results in the locked order: rules in model order → per rule, red Violations then Grandfathered (both
    // already ordered by ArchChecker.Order) → each violation's Sites in stored order (one result per site).
    private static IReadOnlyList<SarifResult> BuildResults(CheckReport report, string solutionDirectory)
    {
        var results = new List<SarifResult>();
        foreach (RuleResult result in report.Results)
        {
            foreach (Violation violation in result.Violations)
                results.AddRange(SiteResults(result.Rule.Id, violation, ErrorLevel, "new", null, solutionDirectory));

            // Grandfathered is index-aligned with GrandfatheredEntries (RuleResult invariant), so entry i
            // is the stored baseline entry that blessed violation i — its because becomes the justification.
            for (var i = 0; i < result.Grandfathered.Count; i++)
            {
                Violation violation = result.Grandfathered[i];
                BaselineEntry entry = result.GrandfatheredEntries[i];
                string justification = entry.Because ?? $"grandfathered in {result.Rule.BaselinePath}";
                IReadOnlyList<SarifSuppression> suppressions = new[] { new SarifSuppression("external", justification) };
                results.AddRange(SiteResults(result.Rule.Id, violation, NoteLevel, "unchanged", suppressions, solutionDirectory));
            }
        }

        return results;
    }

    // One result per site. The partial fingerprint keys the alert as (ruleId, v1|source|target|subject|rel|ord)
    // — the identity slots from BaselineIdentity() (empty slots allowed), and a per-file ordinal reset for each
    // violation, so multiple sites of one violation in one file stay distinct while code motion does not churn it.
    private static IEnumerable<SarifResult> SiteResults(
        string ruleId,
        Violation violation,
        string level,
        string baselineState,
        IReadOnlyList<SarifSuppression>? suppressions,
        string solutionDirectory)
    {
        BaselineEntry? identity = violation.BaselineIdentity();
        string source = identity?.Source ?? string.Empty;
        string target = identity?.Target ?? string.Empty;
        string subject = identity?.Subject ?? string.Empty;
        var message = new SarifMessage(MessageText(violation));
        var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (SourceLocation site in violation.Sites)
        {
            string relativePath = PathFormat.Relative(solutionDirectory, site.FilePath);
            int ordinal = ordinals.TryGetValue(relativePath, out int seen) ? seen : 0;
            ordinals[relativePath] = ordinal + 1;

            var fingerprints = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FingerprintKey] = $"v1|{source}|{target}|{subject}|{relativePath}|{ordinal}"
            };
            var location = new SarifLocation(
                new SarifPhysicalLocation(
                    new SarifArtifactLocation(relativePath, SrcRootBaseId),
                    new SarifRegion(site.Line)));

            yield return new SarifResult(
                ruleId, level, message, new[] { location }, fingerprints, baselineState, suppressions);
        }
    }

    // The per-kind result body, duplicated from HumanReportRenderer per house convention (each renderer
    // formats independently). EmptySubject/RuleError are site-less, so this is never reached for them.
    private static string MessageText(Violation violation)
    {
        return violation.Kind switch
        {
            ViolationKind.Reference => $"{violation.Source!.FullName} references {violation.Target!.FullName}",
            ViolationKind.MemberUse => $"{violation.Source!.FullName} uses {MemberDisplay(violation.Member!)}",
            ViolationKind.Construction => $"{violation.Source!.FullName} constructs {violation.Target!.FullName}",
            ViolationKind.Injection => $"{violation.Source!.FullName} injects {violation.Target!.FullName}",
            ViolationKind.Catch => $"{violation.Source!.FullName} catches {violation.Target!.FullName}",
            ViolationKind.Throw => $"{violation.Source!.FullName} throws {violation.Target!.FullName}",
            ViolationKind.Expose => $"{violation.Source!.FullName} exposes {violation.Target!.FullName}",
            ViolationKind.Shape => violation.Subject!.FullName,
            ViolationKind.MemberShape => MemberSubjectDisplay(violation.SubjectMember!),
            _ => string.Empty
        };
    }

    // declaring-type-dot-member, with () appended iff a method — the human analog of the §6 prose form.
    private static string MemberDisplay(MemberReference member)
    {
        string suffix = member.Kind == MemberKind.Method ? "()" : string.Empty;
        return $"{member.ContainingType.FullName}.{member.Name}{suffix}";
    }

    private static string MemberSubjectDisplay(MemberNode member)
    {
        string suffix = member.Kind == MemberKind.Method ? "()" : string.Empty;
        return $"{((TypeNode)member.DeclaringType).FullName}.{member.Name}{suffix}";
    }

    private static string Camel(string name)
    {
        return name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);
    }
}