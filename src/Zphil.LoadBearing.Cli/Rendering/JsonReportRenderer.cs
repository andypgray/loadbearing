using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Rendering;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     Renders a <see cref="CheckReport" /> as the <c>--json</c> document (schemaVersion 3 — Freeze
///     containment now evaluates and ratchets alongside Migrate, and a Freeze tripwire warns) — the only
///     content on stdout in JSON mode, so hooks can parse it. The optional <c>diffBase</c> echoes the
///     <c>--diff-base</c> ref (omitted when absent). Machine-independent: <c>solution</c> and
///     <c>specAssembly</c> are file names, and every site path is solution-relative with forward slashes.
///     Serialization lives here so Core stays dependency-free.
/// </summary>
internal static class JsonReportRenderer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Backticks and em-dashes ride in rule sentences; this is CLI output for hooks, not HTML, so
        // emit them literally rather than as ` escapes.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void Render(
        TextWriter output,
        CheckReport report,
        string solutionDirectory,
        string solutionName,
        string specAssembly,
        string? diffBase,
        IReadOnlyList<string> workspaceDiagnostics)
    {
        var document = new CheckJson(
            3,
            solutionName,
            specAssembly,
            diffBase,
            report.Results.Select(r => ToRule(r, solutionDirectory)).ToList(),
            workspaceDiagnostics,
            new SummaryJson(
                report.RulesChecked, report.RulesPassed, report.RulesFailed, report.RulesSkipped,
                report.ViolationCount, report.WarningCount));

        output.WriteLine(JsonSerializer.Serialize(document, Options));
    }

    private static RuleJson ToRule(RuleResult result, string solutionDirectory)
    {
        return new RuleJson(
            result.Rule.Id,
            Camel(result.Rule.Posture.ToString()),
            Camel(result.Status.ToString()),
            result.Rule.Sentence,
            result.Rule.Because,
            result.Rule.Fix,
            result.SkipReason,
            ToBaseline(result),
            result.Violations.Select(v => ToViolation(v, solutionDirectory)).ToList(),
            result.Warnings.Select(w => new WarningJson(Camel(w.Kind.ToString()), w.Message)).ToList());
    }

    // The baseline block is present for any ratcheted rule (Migrate or Freeze containment); the model's
    // relative path string rides through verbatim.
    private static BaselineJson? ToBaseline(RuleResult result)
    {
        return result.Rule.BaselinePath is { } path
            ? new BaselineJson(path, result.Grandfathered.Count, result.StaleBaselineEntries)
            : null;
    }

    private static ViolationJson ToViolation(Violation violation, string solutionDirectory)
    {
        return new ViolationJson(
            Camel(violation.Kind.ToString()),
            violation.Source?.FullName,
            violation.Target?.FullName,
            violation.Subject?.FullName,
            violation.Detail,
            violation.Sites.Select(s => new SiteJson(PathFormat.Relative(solutionDirectory, s.FilePath), s.Line)).ToList());
    }

    private static string Camel(string name)
    {
        return name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);
    }
}