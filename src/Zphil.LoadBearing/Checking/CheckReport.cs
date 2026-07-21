namespace Zphil.LoadBearing.Checking;

/// <summary>
///     The full result of a check run: one <see cref="RuleResult" /> per rule in model (authoring)
///     order, plus roll-up counts. This is the single object both render targets consume — the human
///     and JSON CLI renderers, and the MCP <c>arch_check</c> tool.
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
        StaleBaselineEntryCount = results.Sum(r => r.StaleBaselineEntries);
    }

    /// <summary>Every rule's result, in model order.</summary>
    public IReadOnlyList<RuleResult> Results { get; }

    /// <summary>Total rules in the report.</summary>
    public int RulesChecked { get; }

    /// <summary>Rules that held.</summary>
    public int RulesPassed { get; }

    /// <summary>Rules that were violated or errored.</summary>
    public int RulesFailed { get; }

    /// <summary>Rules not evaluated — a Freeze tripwire with no <c>--diff-base</c> diff context (GRAMMAR §7).</summary>
    public int RulesSkipped { get; }

    /// <summary>Total <em>red</em> violations across all rules (grandfathered Migrate violations excluded).</summary>
    public int ViolationCount { get; }

    /// <summary>Total warnings across all rules.</summary>
    public int WarningCount { get; }

    /// <summary>Total grandfathered (baselined) Migrate violations across all rules — the burndown remaining.</summary>
    public int GrandfatheredCount { get; }

    /// <summary>Total stale baseline entries across all rules — fixed debt awaiting <c>baseline --accept-reductions</c>.</summary>
    public int StaleBaselineEntryCount { get; }

    /// <summary>Whether any rule failed — the CLI's exit-code-1 signal. Red-only, so a fully grandfathered spec is clean.</summary>
    public bool HasViolations => RulesFailed > 0;
}