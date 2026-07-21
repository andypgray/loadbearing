using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Checking;

/// <summary>
///     Evaluates a finalized <see cref="ArchitectureModel" /> against an extracted
///     <see cref="CodebaseModel" /> — the pure-Core heart of <c>loadbearing check</c> and
///     <c>status</c>. One <see cref="RuleResult" /> per rule, in model order: Enforce rules are
///     evaluated; ratcheted rules — Migrate and Freeze containment — are evaluated the same way and
///     then <em>partitioned</em> against a <see cref="BaselineIndex" /> (in-baseline =
///     grandfathered/pass, not-in-baseline = red, including new code in the old pattern); a Freeze
///     tripwire runs the diff-aware touch check (GRAMMAR §7), warning per changed file
///     inside the scope and passing, or skipping when no <see cref="DiffContext" /> was supplied. Any
///     evaluation error becomes a <see cref="ViolationKind.RuleError" /> (Failed) rather than aborting
///     the run (all-errors philosophy).
/// </summary>
public static class ArchChecker
{
    /// <summary>Pinned skip reason for a Freeze tripwire when no <c>--diff-base</c> diff context is present.</summary>
    internal const string TripwireSkipReason =
        "Tripwire: no diff context — run 'loadbearing check --diff-base <ref>' to check changed files against this frozen scope.";

    /// <summary>Checks every rule with no baselines (every ratchet violation red). See the four-arg overload.</summary>
    public static CheckReport Check(ArchitectureModel model, CodebaseModel codebase)
    {
        return Check(model, codebase, BaselineIndex.Empty, null);
    }

    /// <summary>Checks every rule against <paramref name="baselines" /> with no diff context (tripwires skip).</summary>
    public static CheckReport Check(ArchitectureModel model, CodebaseModel codebase, BaselineIndex baselines)
    {
        return Check(model, codebase, baselines, null);
    }

    /// <summary>
    ///     Checks every rule and returns the aggregate report. Ratchet violations (Migrate, Freeze
    ///     containment) are partitioned against <paramref name="baselines" />: a violation whose
    ///     identity (GRAMMAR §4.3) is in the rule's captured section is grandfathered (it passes);
    ///     anything else is red. A Freeze tripwire warns for each changed file in
    ///     <paramref name="diff" /> that declares a type in the frozen scope, or skips when
    ///     <paramref name="diff" /> is null.
    /// </summary>
    public static CheckReport Check(
        ArchitectureModel model, CodebaseModel codebase, BaselineIndex baselines, DiffContext? diff)
    {
        Guard.NotNull(model, nameof(model));
        Guard.NotNull(codebase, nameof(codebase));
        Guard.NotNull(baselines, nameof(baselines));

        var evaluator = new ConstraintEvaluator(codebase);
        var selections = new SelectionEvaluator(codebase);
        var results = model.Rules.Select(rule => CheckRule(rule, evaluator, selections, baselines, diff)).ToList();
        return new CheckReport(results);
    }

    private static RuleResult CheckRule(
        ArchRule rule, ConstraintEvaluator evaluator, SelectionEvaluator selections, BaselineIndex baselines, DiffContext? diff)
    {
        // The tripwire carries no closed-vocabulary constraint (its Constraint is null and must never
        // reach the evaluator); it is a diff-aware warning check, not a red-producing rule (GRAMMAR §7).
        if (rule.Freeze is { Role: FreezeRole.Tripwire }) return Tripwire(rule, selections, diff);

        try
        {
            var (violations, warnings) = evaluator.Evaluate(rule.Constraint!);
            // A ratcheted rule — Migrate, or Freeze containment (which reifies a real
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
        var ordered = Order(violations);
        RuleStatus status = ordered.Count > 0 ? RuleStatus.Failed : RuleStatus.Passed;
        return new RuleResult(rule, status, ordered, warnings, null, Array.Empty<Violation>(), 0, false);
    }

    // The ratchet, shared by Migrate and Freeze containment: a violation whose identity
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

        var orderedRed = Order(red);
        var orderedGrandfathered = OrderPairs(grandfatheredPairs);
        int stale = section is null ? 0 : section.Count - matched.Count;
        RuleStatus status = orderedRed.Count > 0 ? RuleStatus.Failed : RuleStatus.Passed;
        return new RuleResult(
            rule, status, orderedRed, warnings, null,
            orderedGrandfathered.Select(p => p.Violation).ToList(), stale, captured,
            orderedGrandfathered.Select(p => p.Entry).ToList());
    }

    // The Freeze tripwire (GRAMMAR §7): with no diff context it skips; otherwise it warns once per
    // changed file that declares a type in the frozen selection and always passes (warnings never gate).
    // An empty frozen selection yields zero warnings and passes silently — containment's EmptySubject is
    // the loud misconfiguration channel.
    private static RuleResult Tripwire(ArchRule rule, SelectionEvaluator selections, DiffContext? diff)
    {
        if (diff is null) return Skipped(rule, TripwireSkipReason);

        string scopeId = rule.Freeze!.ScopeId;
        var frozen = selections.Evaluate(rule.Freeze.Frozen!, SelectionPosition.Subject);

        var touched = frozen
            .Where(type => !type.IsExternal)
            .SelectMany(type => type.DeclarationSites)
            .Select(site => site.FilePath)
            .Distinct(StringComparer.Ordinal)
            .Where(diff.Contains)
            .Select(diff.SolutionRelative)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new CheckWarning(CheckWarningKind.FrozenScopeTouched, TripwireMessage(path, scopeId)))
            .ToList();

        return new RuleResult(rule, RuleStatus.Passed, Array.Empty<Violation>(), touched, null, Array.Empty<Violation>(), 0, false);
    }

    private static string TripwireMessage(string relativePath, string scopeId)
    {
        return $"Changed file '{relativePath}' is inside frozen scope '{scopeId}' — does the task actually " +
               $"require editing dragon territory? Dragons: loadbearing explain {scopeId}/tripwire.";
    }

    private static RuleResult Skipped(ArchRule rule, string reason)
    {
        return new RuleResult(
            rule, RuleStatus.Skipped, Array.Empty<Violation>(), Array.Empty<CheckWarning>(), reason,
            Array.Empty<Violation>(), 0, false);
    }

    private static RuleResult Errored(ArchRule rule, string detail)
    {
        return new RuleResult(
            rule, RuleStatus.Failed, new[] { Violation.RuleError(detail) }, Array.Empty<CheckWarning>(), null,
            Array.Empty<Violation>(), 0, false);
    }

    // Deterministic within-rule order: (Source|Subject FullName, Target FullName, Member SymbolId),
    // ordinal. A MemberUse mirrors Reference's (source, target) as (source FullName, member SymbolId); a
    // MemberShape mirrors Shape's subject as (declaring-type FullName, member SymbolId).
    private static IReadOnlyList<Violation> Order(IReadOnlyList<Violation> violations)
    {
        return violations
            .OrderBy(OrderPrimary, StringComparer.Ordinal)
            .ThenBy(OrderSecondary, StringComparer.Ordinal)
            .ThenBy(OrderTertiary, StringComparer.Ordinal)
            .ToList();
    }

    // The ratchet's grandfathered (violation, entry) pairs, ordered by the SAME keys as Order so the two
    // split lists — Grandfathered and its index-aligned GrandfatheredEntries — share order (OrderBy is a
    // stable sort, so this yields exactly the violation order Order would).
    private static List<(Violation Violation, BaselineEntry Entry)> OrderPairs(
        List<(Violation Violation, BaselineEntry Entry)> pairs)
    {
        return pairs
            .OrderBy(p => OrderPrimary(p.Violation), StringComparer.Ordinal)
            .ThenBy(p => OrderSecondary(p.Violation), StringComparer.Ordinal)
            .ThenBy(p => OrderTertiary(p.Violation), StringComparer.Ordinal)
            .ToList();
    }

    // The three ordinal sort keys, single-sourced so Order and OrderPairs cannot drift.
    private static string OrderPrimary(Violation violation)
    {
        return (violation.Source ?? violation.Subject)?.FullName ?? MemberDeclaringFullName(violation);
    }

    private static string OrderSecondary(Violation violation)
    {
        return violation.Target?.FullName ?? string.Empty;
    }

    private static string OrderTertiary(Violation violation)
    {
        return violation.Member?.SymbolId ?? violation.SubjectMember?.SymbolId ?? string.Empty;
    }

    // A MemberShape violation's declaring-type FullName — its primary sort key (empty for every other kind,
    // which already sort on Source/Subject). The declaring type is always the owning TypeNode.
    private static string MemberDeclaringFullName(Violation violation)
    {
        return violation.SubjectMember is { } member ? ((TypeNode)member.DeclaringType).FullName : string.Empty;
    }
}