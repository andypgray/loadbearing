using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     Renders a <see cref="CheckReport" /> as human-readable text (stdout): one block per rule in
///     model order, an ASCII status marker (no ANSI), the rule ID and its sentence, then — for a
///     failed rule — its <c>because</c>/<c>fix</c> and each red violation site (solution-relative,
///     forward-slash paths, ordered by file then line). A ratcheted rule (Migrate or Freeze containment)
///     also gets a grandfathered count (baselined violations pass, so they are not listed as red) and,
///     when it fails uncaptured, the <c>baseline --init</c> bootstrap hint. Ends with a one-line summary.
///     This is the acceptance surface: a failing rule shows ID, because, fix, and <c>file:line</c> together.
///     Lives in Core so the CLI and the xUnit adapter share one failure-text renderer (<see cref="RuleBlock" />).
/// </summary>
public static class HumanReportRenderer
{
    /// <summary>Renders the whole report (every rule block plus the summary line) to <paramref name="output" />.</summary>
    public static void Render(TextWriter output, CheckReport report, string solutionDirectory)
    {
        foreach (RuleResult result in report.Results) RenderRule(output, result, solutionDirectory);

        output.WriteLine();
        output.WriteLine(
            $"Checked {report.RulesChecked} rules: {report.RulesPassed} passed, {report.RulesFailed} failed, " +
            $"{report.RulesSkipped} skipped ({report.ViolationCount} violations, {report.WarningCount} warnings).");
    }

    /// <summary>
    ///     Renders one rule's block — the exact per-rule text <see cref="Render" /> emits, with no trailing
    ///     newline and LF line endings. This is the xUnit adapter's <c>Assert.Fail</c> body, so a failing
    ///     rule reads identically whether it lands via <c>loadbearing check</c> or a named adapter test.
    /// </summary>
    public static string RuleBlock(RuleResult result, string solutionDirectory)
    {
        var writer = new StringWriter { NewLine = "\n" };
        RenderRule(writer, result, solutionDirectory);
        return writer.ToString().TrimEnd('\n');
    }

    private static void RenderRule(TextWriter output, RuleResult result, string solutionDirectory)
    {
        string header = result.Rule.Sentence.Length > 0
            ? $"{Marker(result)} {result.Rule.Id} — {result.Rule.Sentence}"
            : $"{Marker(result)} {result.Rule.Id}";
        output.WriteLine(header);

        if (result.Status == RuleStatus.Skipped)
        {
            output.WriteLine($"  skipped: {result.SkipReason}");
            return;
        }

        if (result.Status == RuleStatus.Failed)
        {
            output.WriteLine($"  because: {result.Rule.Because}");
            if (result.Rule.Fix is { } fix) output.WriteLine($"  fix: {fix}");
            foreach (string line in ViolationLines(result, solutionDirectory)) output.WriteLine($"  {line}");
        }

        if (result.Rule.BaselinePath is not null) RenderRatchetLines(output, result);

        foreach (CheckWarning warning in result.Warnings) output.WriteLine($"  warning: {warning.Message}");
    }

    // The ratchet's human lines (Migrate and Freeze containment): a grandfathered count (baselined
    // violations pass, so they are not listed as red sites) and, when a failed rule has no captured
    // baseline, the bootstrap hint (DESIGN.md §5).
    private static void RenderRatchetLines(TextWriter output, RuleResult result)
    {
        if (result.Grandfathered.Count > 0)
            output.WriteLine($"  grandfathered: {result.Grandfathered.Count} (baselined; run 'loadbearing status' for burndown)");

        if (result.Status == RuleStatus.Failed && !result.BaselineCaptured)
            output.WriteLine(
                "  hint: no baseline captured for this rule; run 'loadbearing baseline --init' to grandfather existing violations");
    }

    private static IEnumerable<string> ViolationLines(RuleResult result, string solutionDirectory)
    {
        var located = new List<(string Path, int Line, string Text)>();
        var unlocated = new List<string>();

        foreach (Violation violation in result.Violations)
            switch (violation.Kind)
            {
                case ViolationKind.Reference:
                    foreach (SourceLocation site in violation.Sites)
                        located.Add((PathFormat.Relative(solutionDirectory, site.FilePath), site.Line,
                            $"{violation.Source!.FullName} references {violation.Target!.FullName}"));
                    break;
                case ViolationKind.MemberUse:
                    foreach (SourceLocation site in violation.Sites)
                        located.Add((PathFormat.Relative(solutionDirectory, site.FilePath), site.Line,
                            $"{violation.Source!.FullName} uses {MemberDisplay(violation.Member!)}"));
                    break;
                case ViolationKind.Shape:
                    SourceLocation? first = violation.Subject!.DeclarationSites.FirstOrDefault();
                    if (first is not null)
                        located.Add((PathFormat.Relative(solutionDirectory, first.FilePath), first.Line, violation.Subject.FullName));
                    else
                        unlocated.Add(violation.Subject.FullName);
                    break;
                case ViolationKind.MemberShape:
                    MemberNode member = violation.SubjectMember!;
                    SourceLocation? at = member.DeclarationSites.FirstOrDefault();
                    string memberLine = MemberSubjectDisplay(member);
                    if (at is not null)
                        located.Add((PathFormat.Relative(solutionDirectory, at.FilePath), at.Line, memberLine));
                    else
                        unlocated.Add(memberLine);
                    break;
                case ViolationKind.EmptySubject:
                    unlocated.Add(violation.Detail ?? "the subject selection matched no types");
                    break;
                case ViolationKind.RuleError:
                    unlocated.Add($"error: {violation.Detail}");
                    break;
            }

        foreach (string text in unlocated) yield return text;

        foreach ((string path, int line, string text) in located
                     .OrderBy(l => l.Path, StringComparer.Ordinal)
                     .ThenBy(l => l.Line)
                     .ThenBy(l => l.Text, StringComparer.Ordinal))
            yield return $"{path}:{line} — {text}";
    }

    // The banned member the source used, as declaring-type-dot-member, with () appended iff a method
    // (never a signature) — the human analog of the §6 prose form, over the extracted MemberReference.
    private static string MemberDisplay(MemberReference member)
    {
        string suffix = member.Kind == MemberKind.Method ? "()" : string.Empty;
        return $"{member.ContainingType.FullName}.{member.Name}{suffix}";
    }

    // The offending member subject, in the same declaring-type-dot-member, () iff a method convention
    // (GRAMMAR §4.6, §6) — over an inventoried MemberNode (its DeclaringType is the owning TypeNode).
    private static string MemberSubjectDisplay(MemberNode member)
    {
        string suffix = member.Kind == MemberKind.Method ? "()" : string.Empty;
        return $"{((TypeNode)member.DeclaringType).FullName}.{member.Name}{suffix}";
    }

    private static string Marker(RuleResult result)
    {
        return result.Status switch
        {
            RuleStatus.Failed => "FAIL",
            RuleStatus.Skipped => "skip",
            _ => result.Warnings.Count > 0 ? "warn" : "pass"
        };
    }
}