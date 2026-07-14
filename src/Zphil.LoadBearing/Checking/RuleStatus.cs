namespace Zphil.LoadBearing.Checking;

/// <summary>The outcome of evaluating one rule against the codebase (Phase 3).</summary>
public enum RuleStatus
{
    /// <summary>The rule holds. It may still carry an inert-target warning (GRAMMAR §4.1).</summary>
    Passed,

    /// <summary>The rule is violated, has an empty subject, or errored during evaluation.</summary>
    Failed,

    /// <summary>The rule was not evaluated — a Migrate or Freeze posture whose semantics land in a later phase.</summary>
    Skipped
}