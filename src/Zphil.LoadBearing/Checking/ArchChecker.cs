using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Hosting;
using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Checking;

/// <summary>
///     Evaluates a finalized <see cref="ArchitectureModel" /> against an extracted
///     <see cref="CodebaseModel" />, yielding one <see cref="RuleResult" /> per rule in model order.
/// </summary>
/// <remarks>
///     Enforce rules are evaluated; ratcheted rules — Migrate and Quarantine containment — are
///     evaluated the same way and then <em>partitioned</em> against a <see cref="BaselineIndex" />
///     (in-baseline = grandfathered/pass, not-in-baseline = red, including new code in the old
///     pattern); a Quarantine tripwire runs the diff-aware touch check (GRAMMAR §7), warning per
///     changed file inside the scope and passing, or skipping when no <see cref="DiffContext" /> was
///     supplied. Any evaluation error becomes a <see cref="ViolationKind.RuleError" /> (Failed) rather
///     than aborting the run (all-errors philosophy). The run's universe is accounted for here too: the
///     evaluator reports what it evaluated and says nothing about the run it ran in, so a rule that
///     selected nothing under a <see cref="NarrowedUniverse" /> is skipped by this type rather than
///     reported differently by that one.
/// </remarks>
public static class ArchChecker
{
    /// <summary>Pinned skip reason for a Quarantine tripwire when no <c>--diff-base</c> diff context is present.</summary>
    internal const string TripwireSkipReason =
        "Tripwire: no diff context — run 'loadbearing check --diff-base <ref>' to check changed files against this quarantined scope.";

    /// <summary>Checks every rule with no baselines, so every ratchet violation is red.</summary>
    /// <param name="model">The finalized model whose rules to evaluate.</param>
    /// <param name="codebase">The extracted codebase to evaluate them against.</param>
    /// <returns>The aggregate report: one <see cref="RuleResult" /> per rule, in model order, plus roll-up counts.</returns>
    public static CheckReport Check(ArchitectureModel model, CodebaseModel codebase)
    {
        return Check(model, codebase, BaselineIndex.Empty, null);
    }

    /// <summary>Checks every rule against <paramref name="baselines" /> with no diff context (tripwires skip).</summary>
    /// <param name="model">The finalized model whose rules to evaluate.</param>
    /// <param name="codebase">The extracted codebase to evaluate them against.</param>
    /// <param name="baselines">The captured baselines the ratcheted rules partition against.</param>
    /// <returns>The aggregate report: one <see cref="RuleResult" /> per rule, in model order, plus roll-up counts.</returns>
    public static CheckReport Check(ArchitectureModel model, CodebaseModel codebase, BaselineIndex baselines)
    {
        return Check(model, codebase, baselines, null);
    }

    /// <summary>
    ///     Checks every rule and returns the aggregate report. Ratchet violations (Migrate, Quarantine
    ///     containment) are partitioned against <paramref name="baselines" />: a violation whose
    ///     identity (GRAMMAR §4.3) is in the rule's captured section is grandfathered (it passes);
    ///     anything else is red. A Quarantine tripwire warns for each changed file in
    ///     <paramref name="diff" /> that declares a type in the quarantined scope, or skips when
    ///     <paramref name="diff" /> is null.
    /// </summary>
    /// <param name="model">The finalized model whose rules to evaluate.</param>
    /// <param name="codebase">The extracted codebase to evaluate them against.</param>
    /// <param name="baselines">The captured baselines the ratcheted rules partition against.</param>
    /// <param name="diff">The changed-file context a Quarantine tripwire warns from, or null to skip it.</param>
    /// <returns>The aggregate report: one <see cref="RuleResult" /> per rule, in model order, plus roll-up counts.</returns>
    public static CheckReport Check(
        ArchitectureModel model, CodebaseModel codebase, BaselineIndex baselines, DiffContext? diff)
    {
        Guard.NotNull(model, nameof(model));

        return Check(model.Rules, codebase, baselines, diff);
    }

    /// <summary>
    ///     Checks exactly <paramref name="rules" /> — the whole model's, or the subset
    ///     <see cref="SelectRules" /> chose — and returns the aggregate report. Each rule is evaluated
    ///     exactly as the whole-model overload evaluates it; a narrowed run is a smaller report of the
    ///     same shape, because <see cref="CheckReport" />'s counters derive from the results it holds.
    ///     The verdict contract is unchanged: any red rule in the subset fails the report.
    /// </summary>
    /// <param name="rules">The rules to evaluate, in the order they are to be reported.</param>
    /// <param name="codebase">The extracted codebase to evaluate them against.</param>
    /// <param name="baselines">The captured baselines the ratcheted rules partition against.</param>
    /// <param name="diff">The changed-file context a Quarantine tripwire warns from, or null to skip it.</param>
    /// <returns>The aggregate report over <paramref name="rules" /> only, in the order they were given.</returns>
    public static CheckReport Check(
        IReadOnlyList<ArchRule> rules, CodebaseModel codebase, BaselineIndex baselines, DiffContext? diff)
    {
        return Check(rules, codebase, baselines, diff, null);
    }

    /// <summary>
    ///     Checks exactly <paramref name="rules" /> over a run whose universe may be smaller than the
    ///     solution: with <paramref name="narrowing" /> supplied, a rule whose whole subject lives in the
    ///     projects a solution filter left out is <see cref="RuleStatus.Skipped" /> rather than red, because
    ///     its empty subject is that filter's doing and not a defect in the spec.
    /// </summary>
    /// <remarks>
    ///     Internal, so the narrowing can be: the public surface is the four overloads above, and a run
    ///     with no filter behaves byte for byte as it always did (they pass <see langword="null" />). Only a
    ///     rule whose <em>every</em> violation is <see cref="ViolationKind.EmptySubject" /> skips — a
    ///     <see cref="ViolationKind.RuleError" /> is a real defect whatever the universe, and every edge and
    ///     shape kind is a positive finding over types that did load.
    /// </remarks>
    /// <param name="rules">The rules to evaluate, in the order they are to be reported.</param>
    /// <param name="codebase">The extracted codebase to evaluate them against.</param>
    /// <param name="baselines">The captured baselines the ratcheted rules partition against.</param>
    /// <param name="diff">The changed-file context a Quarantine tripwire warns from, or null to skip it.</param>
    /// <param name="narrowing">
    ///     The solution filter that answered this run over part of the solution, or null when the run's
    ///     universe is the whole of it.
    /// </param>
    /// <returns>The aggregate report over <paramref name="rules" /> only, in the order they were given.</returns>
    internal static CheckReport Check(
        IReadOnlyList<ArchRule> rules, CodebaseModel codebase, BaselineIndex baselines, DiffContext? diff,
        NarrowedUniverse? narrowing)
    {
        Guard.NotNull(rules, nameof(rules));
        Guard.NotNull(codebase, nameof(codebase));
        Guard.NotNull(baselines, nameof(baselines));

        // One selection evaluator for the whole run — the constraint arms and the tripwire path share it.
        // Its constructor materializes the solution-declared type list and the noun indexes, so building a
        // second would repeat a full pass over the model; it holds no per-rule mutable state.
        var selections = new SelectionEvaluator(codebase);
        var evaluator = new ConstraintEvaluator(codebase, selections);
        List<RuleResult> results = rules
            .Select(rule => CheckRule(rule, evaluator, selections, baselines, diff, narrowing))
            .ToList();
        return new CheckReport(results);
    }

    /// <summary>
    ///     The rules whose ID matches one of <paramref name="ruleIdGlobs" />, in model order — what a
    ///     narrowed check runs, chosen before any evaluation cost is paid.
    /// </summary>
    /// <remarks>
    ///     A pattern matches the whole rule ID as a single ordinal token, where <c>*</c> spans any run of
    ///     characters including the <c>/</c> separator. There is no implicit subtree: <c>legacy/billing</c>
    ///     selects a rule with exactly that ID and none of its children, while <c>legacy/billing/*</c>
    ///     selects the children a Quarantine scope desugars into (GRAMMAR §7). An empty glob list selects
    ///     every rule, so an unfiltered call costs nothing.
    /// </remarks>
    /// <param name="model">The finalized model to select from.</param>
    /// <param name="ruleIdGlobs">The rule-ID globs; empty means every rule.</param>
    /// <returns>The selected rules, in model order.</returns>
    public static IReadOnlyList<ArchRule> SelectRules(ArchitectureModel model, IReadOnlyList<string> ruleIdGlobs)
    {
        Guard.NotNull(model, nameof(model));
        Guard.NotNull(ruleIdGlobs, nameof(ruleIdGlobs));

        if (ruleIdGlobs.Count == 0) return model.Rules;

        return model.Rules
            .Where(rule => ruleIdGlobs.Any(glob => Wildcard.Match(glob, rule.Id)))
            .ToList();
    }

    private static RuleResult CheckRule(
        ArchRule rule, ConstraintEvaluator evaluator, SelectionEvaluator selections, BaselineIndex baselines,
        DiffContext? diff, NarrowedUniverse? narrowing)
    {
        // The tripwire carries no closed-vocabulary constraint (its Constraint is null and must never
        // reach the evaluator); it is a diff-aware warning check, not a red-producing rule (GRAMMAR §7).
        if (rule.Quarantine is { Role: QuarantineRole.Tripwire }) return Tripwire(rule, selections, diff);

        try
        {
            (IReadOnlyList<Violation> violations, IReadOnlyList<CheckWarning> warnings) = evaluator.Evaluate(rule.Constraint!);
            // Ahead of the ratchet fork, because the question it answers — did this run have the subject in
            // view at all — is asked of the raw violations and is the same one for both branches.
            if (narrowing is not null && SelectedNothing(violations))
                return NarrowedSkip(rule, warnings, baselines, narrowing);

            // A ratcheted rule — Migrate, or Quarantine containment (which reifies a real
            // MustOnlyBeReferencedBy constraint and so evaluates exactly like Enforce) — partitions
            // against its baseline; everything else is plain Enforce law (GRAMMAR §7).
            return rule.BaselinePath is not null
                ? Ratchet(rule, violations, warnings, baselines)
                : Enforce(rule, violations, warnings);
        }
        catch (RuleEvaluationException ex)
        {
            return Errored(rule, ex.Message);
        }
        catch (UnrepresentableTypeException ex)
        {
            return Errored(rule, ex.Message);
        }
        catch (Exception ex)
        {
            // Defensive: an unexpected fault in one rule must not crash the whole run.
            return Errored(rule, $"Rule evaluation failed: {ex.Message}");
        }
    }

    private static RuleResult Enforce(ArchRule rule, IReadOnlyList<Violation> violations, IReadOnlyList<CheckWarning> warnings)
    {
        IReadOnlyList<Violation> ordered = Order(violations);
        RuleStatus status = ordered.Count > 0 ? RuleStatus.Failed : RuleStatus.Passed;
        return new RuleResult(rule, status, ordered, warnings);
    }

    // The ratchet, shared by Migrate and Quarantine containment: a violation whose identity
    // is grandfathered by the rule's captured baseline section passes; everything else — including a new
    // forbidden target from a grandfathered source (pair identity, GRAMMAR §4.3) and every
    // EmptySubject/RuleError (never baselinable) — is red.
    private static RuleResult Ratchet(
        ArchRule rule, IReadOnlyList<Violation> violations, IReadOnlyList<CheckWarning> warnings, BaselineIndex baselines)
    {
        bool captured = baselines.TryGet(rule.Id, out RuleBaseline? section);
        var red = new List<Violation>();
        var grandfatheredPairs = new List<(Violation Violation, BaselineEntry Entry)>();
        var matched = new HashSet<BaselineEntry>();

        foreach (Violation violation in violations)
        {
            BaselineEntry? key = violation.BaselineIdentity();
            // Pair the grandfathered violation with the STORED entry (not the synthesized key) so the
            // entry's attribution reaches the report; matched still keys on the identity, so the stale
            // count is unchanged whether or not the entry carried a because.
            if (key is not null && section is not null && section.TryMatch(key, out BaselineEntry? stored))
            {
                grandfatheredPairs.Add((violation, stored!));
                matched.Add(key);
            }
            else
            {
                red.Add(violation);
            }
        }

        IReadOnlyList<Violation> orderedRed = Order(red);
        List<(Violation Violation, BaselineEntry Entry)> orderedGrandfathered = OrderPairs(grandfatheredPairs);

        // Split in one pass so the two lists are index-aligned by construction: Grandfathered[i] is the
        // violation the entry at GrandfatheredEntries[i] blessed.
        var grandfathered = new List<Violation>(orderedGrandfathered.Count);
        var grandfatheredEntries = new List<BaselineEntry>(orderedGrandfathered.Count);
        for (var i = 0; i < orderedGrandfathered.Count; i++)
        {
            grandfathered.Add(orderedGrandfathered[i].Violation);
            grandfatheredEntries.Add(orderedGrandfathered[i].Entry);
        }

        int stale = section is null ? 0 : section.Count - matched.Count;
        RuleStatus status = orderedRed.Count > 0 ? RuleStatus.Failed : RuleStatus.Passed;
        return new RuleResult(
            rule, status, orderedRed, warnings, null, grandfathered, stale, captured, grandfatheredEntries);
    }

    // The Quarantine tripwire (GRAMMAR §7): with no diff context it skips; otherwise it warns once per
    // changed file that declares a type in the quarantined selection and always passes (warnings never gate).
    // An empty quarantined selection yields zero warnings and passes silently — containment's EmptySubject is
    // the loud misconfiguration channel.
    private static RuleResult Tripwire(ArchRule rule, SelectionEvaluator selections, DiffContext? diff)
    {
        if (diff is null) return Skipped(rule, TripwireSkipReason);

        string scopeId = rule.Quarantine!.ScopeId;
        HashSet<TypeNode> quarantined = selections.Evaluate(rule.Quarantine.Quarantined!, SelectionPosition.Subject);

        List<CheckWarning> touched = quarantined
            .Where(type => !type.IsExternal)
            .SelectMany(type => type.DeclarationSites)
            .Select(site => site.FilePath)
            .Distinct(StringComparer.Ordinal)
            .Where(diff.Contains)
            .Select(diff.SolutionRelative)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new CheckWarning(CheckWarningKind.QuarantinedScopeTouched, TripwireMessage(path, scopeId)))
            .ToList();

        return new RuleResult(rule, RuleStatus.Passed, Array.Empty<Violation>(), touched);
    }

    private static string TripwireMessage(string relativePath, string scopeId)
    {
        return $"Changed file '{relativePath}' is inside quarantined scope '{scopeId}' — does the task actually " +
               $"require editing dragon territory? Dragons: loadbearing explain {scopeId}/tripwire.";
    }

    // A rule the narrowed run never had in view: its subject selection matched nothing, and nothing is
    // exactly what a filter that dropped whole projects would produce. Only "every violation is an empty
    // subject" qualifies — one real finding means types did load and the rule was measured.
    private static bool SelectedNothing(IReadOnlyList<Violation> violations)
    {
        return violations.Count > 0 && violations.All(violation => violation.Kind == ViolationKind.EmptySubject);
    }

    // The narrowing skip, minted apart from Skipped() above, which drops warnings and hardcodes an
    // uncaptured baseline. Here the warnings still hold (they are facts about the projects that did load),
    // BaselineCaptured stays truthful — and the ratchet counts are forced to nothing, because Ratchet would
    // otherwise report the whole captured section as "fixed awaiting acceptance" for a rule this run never
    // measured, which is precisely the reduction 'baseline --accept-reductions' must never be offered.
    private static RuleResult NarrowedSkip(
        ArchRule rule, IReadOnlyList<CheckWarning> warnings, BaselineIndex baselines, NarrowedUniverse narrowing)
    {
        bool captured = rule.BaselinePath is not null && baselines.TryGet(rule.Id, out _);
        return new RuleResult(
            rule, RuleStatus.Skipped, Array.Empty<Violation>(), warnings, narrowing.RuleSkipReason,
            baselineCaptured: captured);
    }

    private static RuleResult Skipped(ArchRule rule, string reason)
    {
        return new RuleResult(rule, RuleStatus.Skipped, Array.Empty<Violation>(), skipReason: reason);
    }

    private static RuleResult Errored(ArchRule rule, string detail)
    {
        return new RuleResult(rule, RuleStatus.Failed, [Violation.RuleError(detail)]);
    }

    // Deterministic within-rule order: Violation.OrderKey, compared ordinal slot by slot (never as a
    // tuple, whose default string comparison is culture-sensitive).
    private static IReadOnlyList<Violation> Order(IReadOnlyList<Violation> violations)
    {
        return violations
            .OrderBy(v => v.OrderKey.Primary, StringComparer.Ordinal)
            .ThenBy(v => v.OrderKey.Secondary, StringComparer.Ordinal)
            .ThenBy(v => v.OrderKey.Tertiary, StringComparer.Ordinal)
            .ToList();
    }

    // The ratchet's grandfathered (violation, entry) pairs, ordered by the SAME keys as Order so the two
    // split lists — Grandfathered and its index-aligned GrandfatheredEntries — share order (OrderBy is a
    // stable sort, so this yields exactly the violation order Order would).
    private static List<(Violation Violation, BaselineEntry Entry)> OrderPairs(
        List<(Violation Violation, BaselineEntry Entry)> pairs)
    {
        return pairs
            .OrderBy(p => p.Violation.OrderKey.Primary, StringComparer.Ordinal)
            .ThenBy(p => p.Violation.OrderKey.Secondary, StringComparer.Ordinal)
            .ThenBy(p => p.Violation.OrderKey.Tertiary, StringComparer.Ordinal)
            .ToList();
    }
}
