using Zphil.LoadBearing.Baselines;
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
            if (result.Rule.Citation is { } citation) output.WriteLine($"  citation: {citation}");
            if (result.Rule.Fix is { } fix) output.WriteLine($"  fix: {fix}");
        }

        RenderSubjectLine(output, result);

        // Split from the because/fix block above rather than joined to it: the subject line renders for a
        // passing rule too, and it belongs with the rule's own framing rather than after its site list.
        if (result.Status == RuleStatus.Failed)
            foreach (string line in ViolationLines(result, relativizer))
                output.WriteLine($"  {line}");

        if (result.Rule.BaselinePath is not null) RenderRatchetLines(output, result);

        foreach (CheckWarning warning in result.Warnings) output.WriteLine($"  warning: {warning.Message}");

        RenderDragonsLines(output, result);
    }

    // The dragons themselves, under a tripwire that actually fired. The warning above says a changed file is
    // in dragon territory and points at `explain` for the prose; that is one round trip too many when the
    // reader is an agent mid-edit, and the prose is already on the rule. Once per rule, never per warning —
    // the dragons are a fact about the scope, not about which file was touched — and absent entirely when
    // nothing fired, so a silent tripwire still renders as one line.
    private static void RenderDragonsLines(TextWriter output, RuleResult result)
    {
        if (result.Rule.Scope is not { Role: ScopeRole.Tripwire } scope || result.Warnings.Count == 0) return;

        if (scope.Dragons is { } dragons) output.WriteLine($"  dragons: {dragons}");
        if (scope.DragonsDoc is { } dragonsDoc) output.WriteLine($"  dragons-doc: {dragonsDoc}");
    }

    // What the rule's subject actually swept, stated only when some of it is generator output — so a rule
    // aimed squarely at code someone wrote says nothing, and the line's presence is itself the finding.
    // It is a fact, never advice: .Authored() is the cure often enough to name in the docs and wrong often
    // enough that a nag here would be noise on every green run.
    private static void RenderSubjectLine(TextWriter output, RuleResult result)
    {
        if (result.SubjectGeneratedTypes == 0) return;

        string types = Plurals.Noun(result.SubjectTypes, "type");
        output.WriteLine($"  subject: {result.SubjectTypes} {types}, {result.SubjectGeneratedTypes} generated");
    }

    // The ratchet's human lines (Migrate and Quarantine containment). Baselined violations pass, so they
    // are never listed as red sites; the grandfathered count is the only place a reader sees them. A grown
    // pair is red and so already listed above, and the grandfathered count deliberately leaves it out:
    // it counts what passed, and a pair that exceeded its allowance did not.
    private static void RenderRatchetLines(TextWriter output, RuleResult result)
    {
        if (result.Grandfathered.Count > 0)
            output.WriteLine($"  grandfathered: {result.Grandfathered.Count} (baselined; run 'loadbearing status' for burndown)");

        RenderGrownLine(output, result);

        if (result is { Status: RuleStatus.Failed, BaselineCaptured: false })
            output.WriteLine(
                "  hint: no baseline captured for this rule; run 'loadbearing baseline --init' to grandfather existing violations");
    }

    // Why a red site sits on a pair the baseline names. Without this line a reader who looks the pair up
    // finds it captured and reads the report as wrong; with it, the allowance and what the run measured
    // against it are both on the page. The two numbers are sums over the grown pairs, so one line covers
    // however many grew — and it never says which site is the new one, because the count does not know:
    // that lossiness is what buys the measure its immunity to line churn.
    private static void RenderGrownLine(TextWriter output, RuleResult result)
    {
        int grown = result.GrownBaselineEntries;
        if (grown == 0) return;

        var baselined = 0;
        var observed = 0;
        foreach (KeyValuePair<Violation, BaselineEntry> pair in result.GrownEntries)
        {
            baselined += pair.Value.SiteCount ?? 0;
            observed += pair.Key.Sites.Count;
        }

        string pairs = Plurals.Noun(grown, "pair");
        output.WriteLine(
            $"  grown: {grown} grandfathered {pairs} exceeded the baseline site count "
            + $"({baselined} baselined, {observed} observed)");
    }

    private static IEnumerable<string> ViolationLines(RuleResult result, PathFormat.Relativizer relativizer)
    {
        var located = new List<(string Path, int Line, string Text)>();
        var unlocated = new List<string>();

        // Where a one-line violation lands: its first carried site as a jump target, or the unlocated
        // block when it carries none. The site always comes off the violation — every shape kind's
        // evidence is minted where the verb decided it points, and the renderer only reads it.
        void Place(SourceLocation? at, string text)
        {
            if (at is not null)
                located.Add((relativizer.Relative(at.FilePath), at.Line, text));
            else
                unlocated.Add(text);
        }

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
                // The three subject kinds place identically — one line at the first site the verb carried,
                // or the unlocated block — so they share the arm and differ only in the text SubjectText picks.
                case ViolationKind.Shape or ViolationKind.MemberShape or ViolationKind.ProjectShape:
                    Place(violation.Sites.FirstOrDefault(), SubjectText(violation));
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

    // What each of the three subject kinds says, with placement left to the shared Place above. The project
    // arm is the Shape parallel over a project: its name, at whichever declaration carried the fact the verb
    // read — which may be a props file above the project, and may be nothing at all. A per-package violation
    // names the package too, in EdgeText's register (no backticks: these lines are jump targets, not prose).
    private static string SubjectText(Violation violation)
    {
        return violation.Kind switch
        {
            ViolationKind.MemberShape => MemberText(
                violation.SubjectMember!.DeclaringTypeFullName, violation.SubjectMember.Name,
                violation.SubjectMember.Kind),
            ViolationKind.ProjectShape => violation.Package is { } package
                ? $"{violation.SubjectProject!.Name} references package {package.Name}"
                : violation.SubjectProject!.Name,
            _ => violation.Subject!.FullName
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
