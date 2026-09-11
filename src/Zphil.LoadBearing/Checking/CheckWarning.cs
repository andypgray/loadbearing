namespace Zphil.LoadBearing.Checking;

/// <summary>
///     A note about a rule that still passed: the rule's <see cref="RuleResult.Status" /> stays
///     <see cref="RuleStatus.Passed" />, and warnings never change the check's exit code. Read one
///     rule's warnings from <see cref="RuleResult.Warnings" /> and the run's total from
///     <see cref="CheckReport.WarningCount" />. Each is printed by the check report, carried in
///     <c>--json</c> output, and published as a warning-level result in SARIF output.
/// </summary>
public sealed class CheckWarning
{
    internal CheckWarning(CheckWarningKind kind, string message, string? file = null)
    {
        Kind = kind;
        Message = message;
        File = file;
    }

    /// <summary>
    ///     Gets what raised the warning: a rule whose forbidden target matched no types and so can never
    ///     fire, or a changed file that declares a type inside a quarantined or a cautioned scope.
    /// </summary>
    public CheckWarningKind Kind { get; }

    /// <summary>
    ///     Gets the one line the check report, <c>--json</c> output and SARIF output print for this warning.
    /// </summary>
    public string Message { get; }

    /// <summary>
    ///     Gets the file the warning is about, relative to the solution directory and with forward slashes:
    ///     the same path <see cref="Message" /> names, in a form a host can use without reading it back out
    ///     of prose, and the location SARIF output reports the warning at. Null when the warning is about
    ///     the spec rather than about the code, which is every
    ///     <see cref="CheckWarningKind.InertTarget" /> warning.
    /// </summary>
    // Solution-relative because that is the one spelling every surface can use: an absolute path would
    // have to be relativized again by each of them, and against the same solution directory the tripwire
    // already resolved it against.
    public string? File { get; }
}
