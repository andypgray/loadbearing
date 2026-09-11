namespace Zphil.LoadBearing.Checking;

/// <summary>A non-fatal diagnostic on a rule that still <see cref="RuleStatus.Passed" /> (GRAMMAR §4.1).</summary>
public sealed class CheckWarning
{
    internal CheckWarning(CheckWarningKind kind, string message, string? file = null)
    {
        Kind = kind;
        Message = message;
        File = file;
    }

    /// <summary>The warning kind.</summary>
    public CheckWarningKind Kind { get; }

    /// <summary>The human-readable message.</summary>
    public string Message { get; }

    /// <summary>
    ///     The file the warning is about — solution-relative, forward slashes — or null when it is about the
    ///     spec rather than the code (<see cref="CheckWarningKind.InertTarget" />).
    /// </summary>
    /// <remarks>
    ///     The same path <see cref="Message" /> names, carried structurally so a renderer that needs a
    ///     location does not have to read one out of prose. Solution-relative because that is the only
    ///     spelling every surface can use: an absolute path would have to be relativized again by each of
    ///     them, and against the same solution directory the tripwire already resolved it against.
    /// </remarks>
    public string? File { get; }
}
