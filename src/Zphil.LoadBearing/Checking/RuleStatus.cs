namespace Zphil.LoadBearing.Checking;

/// <summary>The outcome of evaluating one rule against the codebase.</summary>
public enum RuleStatus
{
    /// <summary>The rule holds. It may still carry an inert-target warning (GRAMMAR §4.1).</summary>
    Passed,

    /// <summary>The rule is violated, has an empty subject, or errored during evaluation.</summary>
    Failed,

    /// <summary>The rule was not evaluated on this run — a Freeze tripwire with no diff context (GRAMMAR §7).</summary>
    Skipped
}