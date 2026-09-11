namespace Zphil.LoadBearing.Checking;

/// <summary>
///     A note about a rule that still passed: the rule's <see cref="RuleResult.Status" /> stays
///     <see cref="RuleStatus.Passed" />, and warnings never change the check's exit code. Read one
///     rule's warnings from <see cref="RuleResult.Warnings" /> and the run's total from
///     <see cref="CheckReport.WarningCount" />. Each is printed by the check report, carried in
///     <c>--json</c> output, and published as a warning-level result in SARIF output, and one that carries
///     a <see cref="Hint" /> prints it beside the message on the first two.
/// </summary>
public sealed class CheckWarning
{
    internal CheckWarning(CheckWarningKind kind, string message, string? file = null, string? hint = null)
    {
        Kind = kind;
        Message = message;
        File = file;
        Hint = hint;
    }

    /// <summary>
    ///     Gets what raised the warning: a rule whose forbidden target matched no types and so can never
    ///     fire, or a changed file that declares a type inside a quarantined or a cautioned scope.
    /// </summary>
    public CheckWarningKind Kind { get; }

    /// <summary>
    ///     Gets the line saying what was noticed. The check report, <c>--json</c> output and SARIF output all
    ///     carry it; the first two also carry <see cref="Hint" /> beside it where there is one.
    /// </summary>
    public string Message { get; }

    /// <summary>
    ///     Gets what to change in response to the warning, or null where there is nothing general to advise —
    ///     which is every warning about a file that changed. Present on a warning about a rule whose target
    ///     matched nothing, where it carries the selection's own semantics and the choice left to make. The
    ///     check report prints it on a line under the warning, and <c>--json</c> output carries it as
    ///     <c>hint</c>; SARIF output does not, its reader being an alert surface rather than the author of
    ///     the rule.
    /// </summary>
    public string? Hint { get; }

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
