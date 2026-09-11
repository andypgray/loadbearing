namespace Zphil.LoadBearing.Checking;

/// <summary>
///     What a <see cref="CheckWarning" /> is about. A warning is something the check noticed without
///     failing anything: the rule it sits on still passes, and the exit code is unaffected.
/// </summary>
public enum CheckWarningKind
{
    /// <summary>
    ///     A forbidden-set dependency verb's target pattern matched no types, so the rule can never fire
    ///     and is guarding nothing. Either the pattern is wrong or what it forbids does not exist yet. A
    ///     target named by <c>typeof</c> and absent from the codebase warns about nothing: that is the
    ///     rule working. The <c>MustOnly</c> verbs never warn; an empty allow-list is loud on its own.
    /// </summary>
    InertTarget,

    /// <summary>
    ///     A file changed since the diff base (<c>check --diff-base</c>) declares a type inside a quarantined
    ///     scope, asking whether the task really requires editing dragon territory at all. The rule itself
    ///     still passes.
    /// </summary>
    QuarantinedScopeTouched,

    /// <summary>
    ///     A file changed since the diff base (<c>check --diff-base</c>) declares a type inside a cautioned
    ///     scope, asking that what the scope says is strange inside it be read before editing. Its own kind
    ///     rather than the quarantine's, because the two ask different things; and nothing about a cautioned
    ///     scope ever fails the check.
    /// </summary>
    CautionedScopeTouched
}
