using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Rendering;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     Formats a <see cref="CheckReport" /> as the human <c>status</c> report — one line per rule plus a
///     burndown summary.
/// </summary>
/// <remarks>
///     Pure over the report (no workspace), so the line shapes are unit-pinned. Enforce
///     rules read <c>pass</c>/<c>FAIL</c> with violation/warning counts; a ratcheted rule (Migrate or
///     Quarantine containment) reads the ratchet state — grandfathered remaining, new (red), and
///     fixed-awaiting-acceptance, plus the three measure terms where they apply: the site total behind the
///     remaining pairs, and how many matched entries came in under their recorded count or record none at
///     all. Each of those three extinguishes itself, so a rule whose pairs are one site apiece and whose
///     counts all hold reads exactly as it did before the measure existed. A ratcheted rule over a family
///     (<c>arch.Each</c>) with debt left adds one indented line under its row naming the layers or projects
///     that still hold it, so the burndown says where the work is and not only how much of it there is.
///     A ratcheted rule whose own selection matched nothing reads <c>not measured</c> where it would have
///     read <c>fixed awaiting acceptance</c>, carries neither baseline nudge — the verb refuses both on such
///     a rule — and adds its own indented line saying those entries are untested rather than paid, with the
///     cure the signal minted. An unratcheted rule that matched nothing reads the row it always read: it has
///     no entries to misdescribe, and <c>check</c> is where its cure is.
///     A scope tripwire reads
///     <c>skip</c> (diff-aware), whichever posture minted it: the role is tested ahead of the posture
///     dispatch, so a caution — whose tripwire is its only child — never falls to the Enforce arm and
///     reads <c>pass</c> for a rule the run never ran.
///     Only Migrate surfaces
///     the promotion suggestion when the baseline has burned to zero — Quarantine→Migrate is a human decision.
///     A rule a solution filter left no subject for reads <c>skip</c> with its reason, whatever its posture:
///     the burndown its posture would otherwise print is a claim about a check this run never made.
/// </remarks>
internal static class StatusFormatter
{
    public static IReadOnlyList<string> Lines(CheckReport report)
    {
        List<string> lines = report.Results.SelectMany(RuleLines).ToList();
        lines.Add(Summary(report));
        return lines;
    }

    // One line per rule, or two where a family rule's remaining debt has somewhere to be, or where the rule
    // matched nothing and its row needs qualifying: the row is byte-for-byte the row it always was, and both
    // sub-lines ride under it rather than inside it.
    private static IEnumerable<string> RuleLines(RuleResult result)
    {
        yield return RuleLine(result);

        // Ahead of the split, because it says whether the row above means anything at all, and a breakdown
        // of counts nothing measured would be read before the sentence denying them. Nothing produces both
        // today — an inert target yields no violations to tolerate, and an empty subject's one violation is
        // never baselinable — but the order is the reading order either way.
        if (UnmeasuredLine(result) is { } unmeasured) yield return unmeasured;

        if (CellsLine(result) is { } cells) yield return cells;
    }

    // What the row's "not measured" term means, for a ratcheted rule whose own selection came up empty, or
    // null for every other rule. status is the burndown surface, so it is where a rotted rule is most likely
    // to be read and least likely to be recognized: the counts above are counts of nothing, and the entries
    // they name are untouched debt rather than debt that was paid. The cure comes off the signal itself
    // rather than being minted here, so this line and the check report's `hint:` cannot disagree.
    //
    // Keyed on the baseline like CellsLine below, and for a sharper reason than symmetry: the sentence is
    // about entries, and an Enforce rule has none. Its row is also not the misleading one — it reads the
    // violation or warning count it always read, and the cure for it is on check, where a reader goes for
    // the diagnosis. A ratchet row is the only one that reports rot as a number that looks like progress.
    private static string? UnmeasuredLine(RuleResult result)
    {
        if (!result.Rule.IsRatcheted || !result.SelectionMatchedNothing) return null;

        var diagnosis = "  not measured: the rule's selection matched nothing, so these entries are untested, not fixed.";
        return Cure(result) is { } cure ? $"{diagnosis} {cure}" : diagnosis;
    }

    // The cure the signal already carries — the empty subject's first, since that shape fails the rule and a
    // reader meets it before any warning. Nullable because both slots are: the signals mint a cure at every
    // raise site the product has, and a hand-built result is under no such obligation.
    private static string? Cure(RuleResult result)
    {
        string? subjectCure = result.Violations
            .Where(violation => violation.Kind == ViolationKind.EmptySubject)
            .Select(violation => violation.Hint)
            .FirstOrDefault(hint => hint is not null);

        return subjectCure
               ?? result.Warnings
                   .Where(warning => warning.Kind == CheckWarningKind.InertTarget)
                   .Select(warning => warning.Hint)
                   .FirstOrDefault(hint => hint is not null);
    }

    // The per-cell split of a family rule's burndown, or null when there is none to state. A count alone
    // says how much is left and never where, and on a family that is the reader's next question: one cell
    // holding all but one pair of the debt is the difference between a morning's work and a migration.
    private static string? CellsLine(RuleResult result)
    {
        if (RatchetCells.Of(result) is not { } split) return null;

        IEnumerable<string> cells = split.Cells
            .Select(cell => $"{cell.Name} {cell.Remaining}{Sites(cell.Remaining, cell.RemainingSites)}");

        return $"  {Plurals.Noun(split.Cells.Count, split.CellWord)}: {string.Join(", ", cells)}";
    }

    private static string RuleLine(RuleResult result)
    {
        // A tripwire keeps its own line whatever its status and whichever posture minted it: it says what a
        // diff-aware skip is and what to run to get a verdict, which is the same fact under both.
        if (IsTripwire(result)) return TripwireLine(result);

        // Ahead of the posture dispatch, because posture is what would mislead: a narrowing-skipped Migrate
        // rule falls to RatchetLine and reads "pass … 0 new, 2 fixed awaiting acceptance" — a pass and a
        // burndown for a rule the run never measured.
        if (result.Status == RuleStatus.Skipped)
            return $"skip {result.Rule.Id} — {result.SkipReason}";

        // Keyed on the payload rather than the posture, like every other reader of "is this rule ratcheted"
        // (ArchChecker, the human report, both JSON documents, the baseline verb): a baseline path is what a
        // Migrate rule and a quarantine's containment child have in common, and it is what a posture added
        // later would have to grow to earn the burndown line. Containment ratchets like Migrate but never
        // suggests promotion — RuleResult.Promotable already gates that on the posture, so the label is the
        // only thing left for the posture to supply.
        return result.Rule.IsRatcheted
            ? RatchetLine(result, result.Rule.Posture.ToString().ToLowerInvariant())
            : EnforceLine(result);
    }

    private static bool IsTripwire(RuleResult result)
    {
        return result.Rule.Scope is { Role: ScopeRole.Tripwire };
    }

    // The one tripwire line both scope postures print. A caution's only rule is its tripwire, so the
    // posture arm reaches this directly; a quarantine reaches it past the containment fork. Sharing the
    // string is what keeps the diff-aware skip reading identically wherever the scope came from.
    private static string TripwireLine(RuleResult result)
    {
        return $"skip {result.Rule.Id} (tripwire) — diff-aware; run 'loadbearing check --diff-base <ref>'";
    }

    // The verdict word every non-skip status line opens with. Two-valued, unlike the human report's: a
    // status line's skips take their own arms above, and a warning is not a status the burndown reports.
    private static string Marker(RuleResult result)
    {
        return result.Status == RuleStatus.Failed ? "FAIL" : "pass";
    }

    private static string EnforceLine(RuleResult result)
    {
        var details = new List<string>();
        if (result.Violations.Count > 0) details.Add($"{Plurals.Counted(result.Violations.Count, "violation")}");
        if (result.Warnings.Count > 0) details.Add($"{Plurals.Counted(result.Warnings.Count, "warning")}");

        return details.Count > 0
            ? $"{Marker(result)} {result.Rule.Id} — {string.Join(", ", details)}"
            : $"{Marker(result)} {result.Rule.Id}";
    }

    // Shared by Migrate and Quarantine containment. The promotable branch fires only for Migrate — quarantine
    // promotion (Quarantine→Migrate) is a human decision, so a burned-to-zero containment reads plain. The
    // suggestion is the model's own RuleResult.Promotable rather than a second reading of the same counts,
    // so this line and the status document can never disagree about which rules are ready.
    private static string RatchetLine(RuleResult result, string postureLabel)
    {
        return $"{Marker(result)} {result.Rule.Id} ({postureLabel}) — {RatchetDetail(result)}";
    }

    // The whole result rather than its counts one by one: six ints in a row now feed this line, and a
    // transposed pair of them is a wrong burndown no compiler could have caught.
    private static string RatchetDetail(RuleResult result)
    {
        int remaining = result.Grandfathered.Count;
        int newCount = result.Violations.Count;
        int stale = result.StaleBaselineEntries;

        if (!result.BaselineCaptured)
        {
            var current = $"({newCount} current {Plurals.Noun(newCount, "violation")})";
            // The --init nudge is withheld from a rotted rule for the reason the acceptance nudge is: the verb
            // refuses it, because capturing from a rule that measured nothing signs off "zero debt" nobody
            // saw. What is left is the state, which is true either way.
            return result.SelectionMatchedNothing
                ? $"no baseline captured {current}"
                : $"no baseline captured; run 'loadbearing baseline --init' {current}";
        }

        // Both measures need a matched entry to be counted at all, so a rule with nothing remaining and
        // nothing new has neither — which is why this arm says nothing about them.
        if (newCount == 0 && remaining == 0)
        {
            if (stale == 0)
                return result.Promotable ? "0 remaining; promotable to Enforce (baseline is empty)" : "0 grandfathered remaining";

            // The nudge is withheld with the term, and has to be: on a rotted rule it names the one command
            // that deletes the section outright, which is why the verb now refuses it as well as status no
            // longer offering it. The sub-line under this row says what the entries are instead.
            return result.SelectionMatchedNothing
                ? $"0 remaining, {stale} not measured"
                : $"0 remaining, {stale} fixed awaiting acceptance; run 'loadbearing baseline --accept-reductions'";
        }

        // The measures trail the stale term rather than joining the head of the list: like it, each names a
        // state a write clears.
        string measures = Clause(result.ShrunkBaselineEntries, "shrunk")
                          + Clause(result.UncountedBaselineEntries, "uncounted");
        // A rotted SUBJECT reaches this arm rather than the one above, because its emptiness is itself one
        // new violation — so the honest term is needed in both places, not only where the rule still passes.
        string unmatched = result.SelectionMatchedNothing ? "not measured" : "fixed awaiting acceptance";
        return $"{remaining} grandfathered remaining{Sites(remaining, result.GrandfatheredSiteCount)}, {newCount} new, "
               + $"{stale} {unmatched}{measures}";
    }

    // The site total, worth stating only where it says something the pair count does not. Strictly greater,
    // never merely different: a violation that carries no site at all (a rule error) would otherwise read
    // "(0 sites)", which claims a measurement nobody made.
    private static string Sites(int pairs, int sites)
    {
        return sites > pairs ? $" ({sites} sites)" : string.Empty;
    }

    // A measure clause that extinguishes itself at zero, so a fully counted rule whose counts all still hold
    // prints the line it printed before the measure existed.
    private static string Clause(int count, string term)
    {
        return count > 0 ? $", {count} {term}" : string.Empty;
    }

    // The roll-ups come off the report rather than being re-summed from the rules, so this line and the
    // burndown document can never disagree about the same run.
    private static string Summary(CheckReport report)
    {
        int remaining = report.GrandfatheredCount;
        int uncounted = report.UncountedBaselineEntryCount;

        // The nudge rides on the uncounted clause and nowhere else: it is advice, and once per run beneath
        // the burndown is where it is actionable rather than repeated down every rule line.
        string uncountedClause = uncounted > 0
            ? $", {uncounted} uncounted; run 'loadbearing baseline --accept-reductions' to record site counts"
            : string.Empty;

        string rules = Plurals.Noun(report.RulesChecked, "rule");

        // The rotted share, extinguishing itself at zero like the measure clauses: a run where every rule
        // measured reads exactly as it did before rot had a term. It sits beside the fixed total rather than
        // inside it because the report partitions them — what is here was never tested, and counting it as
        // fixed is what made the roll-up read a rotted rule as progress.
        return $"Checked {report.RulesChecked} {rules}: {report.RulesPassed} passed, {report.RulesFailed} failed, " +
               $"{report.RulesSkipped} skipped. Burndown: {remaining} grandfathered remaining" +
               $"{Sites(remaining, report.GrandfatheredSiteCount)}, " +
               $"{report.StaleBaselineEntryCount} fixed awaiting acceptance" +
               $"{Clause(report.UnmeasuredBaselineEntryCount, "not measured")}" +
               $"{Clause(report.ShrunkBaselineEntryCount, "shrunk")}{uncountedClause}.";
    }
}
