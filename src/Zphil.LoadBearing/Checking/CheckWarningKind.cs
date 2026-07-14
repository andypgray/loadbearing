namespace Zphil.LoadBearing.Checking;

/// <summary>The kind of a non-fatal <see cref="CheckWarning" />.</summary>
public enum CheckWarningKind
{
    /// <summary>
    ///     A forbidden-set dependency verb whose pattern operand resolved to no types — the rule can
    ///     never fire, so it is inert (GRAMMAR §4.1). A bare <c>typeof</c> target absent from the
    ///     codebase is the win condition instead, and stays silent.
    /// </summary>
    InertTarget,

    /// <summary>
    ///     A changed file (from <c>check --diff-base</c>) declares a type in a frozen scope — the Freeze
    ///     tripwire (GRAMMAR §7). The rule still passes; warnings never affect the exit code.
    /// </summary>
    FrozenScopeTouched
}