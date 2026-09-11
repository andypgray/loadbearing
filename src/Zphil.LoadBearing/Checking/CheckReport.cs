namespace Zphil.LoadBearing.Checking;

/// <summary>
///     The full result of a check run: one <see cref="RuleResult" /> per rule in model (authoring)
///     order, plus roll-up counts.
/// </summary>
public sealed class CheckReport
{
    internal CheckReport(IReadOnlyList<RuleResult> results)
    {
        Results = results;
        RulesChecked = results.Count;
        RulesPassed = results.Count(r => r.Status == RuleStatus.Passed);
        RulesFailed = results.Count(r => r.Status == RuleStatus.Failed);
        RulesSkipped = results.Count(r => r.Status == RuleStatus.Skipped);
        ViolationCount = results.Sum(r => r.Violations.Count);
        WarningCount = results.Sum(r => r.Warnings.Count);
        GrandfatheredCount = results.Sum(r => r.Grandfathered.Count);
        GrandfatheredSiteCount = results.Sum(r => r.GrandfatheredSiteCount);
        StaleBaselineEntryCount = results.Sum(r => r.StaleBaselineEntries);
        ShrunkBaselineEntryCount = results.Sum(r => r.ShrunkBaselineEntries);
        UncountedBaselineEntryCount = results.Sum(r => r.UncountedBaselineEntries);
    }

    /// <summary>Every rule's result, in model order.</summary>
    public IReadOnlyList<RuleResult> Results { get; }

    /// <summary>Total rules in the report.</summary>
    public int RulesChecked { get; }

    /// <summary>Rules that held.</summary>
    public int RulesPassed { get; }

    /// <summary>Rules that were violated or errored.</summary>
    public int RulesFailed { get; }

    /// <summary>
    ///     Rules the run reached no verdict for — a scope tripwire with no <c>--diff-base</c> diff
    ///     context (GRAMMAR §7), or a rule whose subject a solution filter left out of the checked universe.
    /// </summary>
    public int RulesSkipped { get; }

    /// <summary>Total <em>red</em> violations across all rules (grandfathered Migrate violations excluded).</summary>
    public int ViolationCount { get; }

    /// <summary>Total warnings across all rules.</summary>
    public int WarningCount { get; }

    /// <summary>Total grandfathered (baselined) Migrate violations across all rules — the burndown remaining.</summary>
    public int GrandfatheredCount { get; }

    /// <summary>
    ///     Total <em>sites</em> carried by those grandfathered violations — the burndown remaining at the
    ///     grain the ratchet measures, which is at least <see cref="GrandfatheredCount" /> and is
    ///     computable whether or not any entry has recorded a count yet.
    /// </summary>
    public int GrandfatheredSiteCount { get; }

    /// <summary>Total stale baseline entries across all rules — fixed debt awaiting <c>baseline --accept-reductions</c>.</summary>
    public int StaleBaselineEntryCount { get; }

    /// <summary>
    ///     Total shrunk baseline entries across all rules — grandfathered pairs now carrying fewer sites
    ///     than they record, awaiting <c>baseline --accept-reductions</c> to lower the count.
    /// </summary>
    public int ShrunkBaselineEntryCount { get; }

    /// <summary>
    ///     Total uncounted baseline entries across all rules — grandfathered edge entries recording no
    ///     site count, so they hold their pair at any size until a write records one.
    /// </summary>
    public int UncountedBaselineEntryCount { get; }

    /// <summary>Whether any rule failed — the CLI's exit-code-1 signal. Red-only, so a fully grandfathered spec is clean.</summary>
    public bool HasViolations => RulesFailed > 0;
}
