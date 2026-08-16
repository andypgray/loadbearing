using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     Renders a <see cref="CheckReport" /> as human-readable text (stdout): one block per rule in
///     model order, an ASCII status marker (no ANSI), the rule ID and its sentence, then — for a
///     failed rule — its <c>because</c>/<c>fix</c> and each red violation site (solution-relative,
///     forward-slash paths, ordered by file then line). Ends with a one-line summary.
/// </summary>
/// <remarks>
///     A ratcheted rule (Migrate or Quarantine containment) also gets a grandfathered count (baselined
///     violations pass, so they are not listed as red) and, when it fails uncaptured, the
///     <c>baseline --init</c> bootstrap hint. This is the acceptance surface: a failing rule shows ID,
///     because, fix, and <c>file:line</c> together.
/// </remarks>
public static class HumanReportRenderer
{
    /// <summary>Renders the whole report (every rule block plus the summary line) to <paramref name="output" />.</summary>
    public static void Render(TextWriter output, CheckReport report, string solutionDirectory)
    {
        // One relativizer for the whole report: the solution directory is the same string for every site,
        // and a migration report on a legacy codebase renders tens of thousands of them.
        var relativizer = new PathFormat.Relativizer(solutionDirectory);
        foreach (RuleResult result in report.Results) RenderRule(output, result, relativizer);

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
        RenderRule(writer, result, new PathFormat.Relativizer(solutionDirectory));
        return writer.ToString().TrimEnd('\n');
    }

    private static void RenderRule(TextWriter output, RuleResult result, PathFormat.Relativizer relativizer)
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
            foreach (string line in ViolationLines(result, relativizer)) output.WriteLine($"  {line}");
        }

        if (result.Rule.BaselinePath is not null) RenderRatchetLines(output, result);

        foreach (CheckWarning warning in result.Warnings) output.WriteLine($"  warning: {warning.Message}");
    }

    // The ratchet's human lines (Migrate and Quarantine containment). Baselined violations pass, so they
    // are never listed as red sites; the grandfathered count is the only place a reader sees them.
    private static void RenderRatchetLines(TextWriter output, RuleResult result)
    {
        if (result.Grandfathered.Count > 0)
            output.WriteLine($"  grandfathered: {result.Grandfathered.Count} (baselined; run 'loadbearing status' for burndown)");

        if (result.Status == RuleStatus.Failed && !result.BaselineCaptured)
            output.WriteLine(
                "  hint: no baseline captured for this rule; run 'loadbearing baseline --init' to grandfather existing violations");
    }

    private static IEnumerable<string> ViolationLines(RuleResult result, PathFormat.Relativizer relativizer)
    {
        var located = new List<(string Path, int Line, string Text)>();
        var unlocated = new List<string>();

        foreach (Violation violation in result.Violations)
            switch (violation.Kind)
            {
                // The seven edge kinds place identically — one line per reference site — so they share the
                // loop and differ only in the verb EdgeText picks.
                case ViolationKind.Reference or ViolationKind.MemberUse or ViolationKind.Construction
                    or ViolationKind.Injection or ViolationKind.Catch or ViolationKind.Throw or ViolationKind.Expose:
                    string edgeText = EdgeText(violation);
                    foreach (SourceLocation site in violation.Sites)
                        located.Add((relativizer.Relative(site.FilePath), site.Line, edgeText));
                    break;
                case ViolationKind.Shape:
                    SourceLocation? first = violation.Subject!.DeclarationSites.FirstOrDefault();
                    if (first is not null)
                        located.Add((relativizer.Relative(first.FilePath), first.Line, violation.Subject.FullName));
                    else
                        unlocated.Add(violation.Subject.FullName);
                    break;
                case ViolationKind.MemberShape:
                    MemberNode member = violation.SubjectMember!;
                    SourceLocation? at = member.DeclarationSites.FirstOrDefault();
                    string memberLine = MemberText(member.DeclaringTypeFullName, member.Name, member.Kind);
                    if (at is not null)
                        located.Add((relativizer.Relative(at.FilePath), at.Line, memberLine));
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

        // Ordinal, not culture-aware: the site order is part of the rendered output, which is diffed and
        // pinned, so it must not shift with the machine's locale.
        foreach ((string path, int line, string text) in located
                     .OrderBy(l => l.Path, StringComparer.Ordinal)
                     .ThenBy(l => l.Line)
                     .ThenBy(l => l.Text, StringComparer.Ordinal))
            yield return $"{path}:{line} — {text}";
    }

    // What each of the seven edge kinds says, with placement left to the shared site loop. The
    // cross-renderer twin of this switch in SarifReportRenderer is a house convention: each renderer
    // formats independently.
    private static string EdgeText(Violation violation)
    {
        return violation.Kind switch
        {
            ViolationKind.Reference => $"{violation.Source!.FullName} references {violation.Target!.FullName}",
            ViolationKind.MemberUse => $"{violation.Source!.FullName} uses {MemberText(violation.Member!.ContainingType.FullName, violation.Member.Name, violation.Member.Kind)}",
            ViolationKind.Construction => $"{violation.Source!.FullName} constructs {violation.Target!.FullName}",
            ViolationKind.Injection => $"{violation.Source!.FullName} injects {violation.Target!.FullName}",
            ViolationKind.Catch => $"{violation.Source!.FullName} catches {violation.Target!.FullName}",
            ViolationKind.Throw => $"{violation.Source!.FullName} throws {violation.Target!.FullName}",
            ViolationKind.Expose => $"{violation.Source!.FullName} exposes {violation.Target!.FullName}",
            _ => string.Empty
        };
    }

    // Declaring-type-dot-member, with () appended iff a method (never a signature) — the human analog of
    // the §6 prose form (GRAMMAR §4.6, §6). One helper for both member shapes: the banned MemberReference
    // a source used, and the offending inventoried MemberNode whose DeclaringType is its owning TypeNode.
    private static string MemberText(string containingFullName, string name, MemberKind kind)
    {
        string suffix = kind == MemberKind.Method ? "()" : string.Empty;
        return $"{containingFullName}.{name}{suffix}";
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
