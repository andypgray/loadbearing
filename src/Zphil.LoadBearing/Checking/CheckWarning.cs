namespace Zphil.LoadBearing.Checking;

/// <summary>A non-fatal diagnostic on a rule that still <see cref="RuleStatus.Passed" /> (GRAMMAR §4.1).</summary>
public sealed class CheckWarning
{
    internal CheckWarning(CheckWarningKind kind, string message)
    {
        Kind = kind;
        Message = message;
    }

    /// <summary>The warning kind.</summary>
    public CheckWarningKind Kind { get; }

    /// <summary>The human-readable message.</summary>
    public string Message { get; }
}