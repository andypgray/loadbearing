namespace Zphil.LoadBearing.Checking;

/// <summary>The outcome of evaluating one rule against the codebase.</summary>
public enum RuleStatus
{
    /// <summary>
    ///     The rule holds. It may still carry a warning — a target pattern that matched nothing, or an edit
    ///     inside a scope.
    /// </summary>
    Passed,

    /// <summary>The rule is violated, its subject matched nothing, or it could not be evaluated.</summary>
    Failed,

    /// <summary>
    ///     The run reached no verdict for this rule: a scope's tripwire with no diff base to compare against
    ///     (pass <c>--diff-base</c> to <c>check</c> to arm it), or a rule whose subject sits in projects the
    ///     run did not cover.
    /// </summary>
    Skipped
}
