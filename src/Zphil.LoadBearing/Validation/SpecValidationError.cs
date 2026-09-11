namespace Zphil.LoadBearing.Validation;

/// <summary>
///     One mistake in a spec: which kind it is (<see cref="Code" />), which rule or scope it is about,
///     where in the spec source it was written, and the message to print. Every error found is reported
///     together, in <see cref="SpecValidationException.Errors" />.
/// </summary>
// Pinned primarily by Code plus RuleId, with one representative Message per code.
public sealed class SpecValidationError
{
    internal SpecValidationError(SpecValidationErrorCode code, string? ruleId, string message, SpecSourceLocation? location = null)
    {
        Code = code;
        RuleId = ruleId;
        Location = location;

        // The location rides in front of the diagnostic (file name and line only) so every error lands at
        // the offending statement — all-errors-at-once is at its best when each one is a jump target. When
        // no location was captured (a spec DLL built against an older Core), the message renders verbatim
        // with no prefix and no leading blank.
        Message = location is null ? message : $"{location}: {message}";
    }

    /// <summary>Gets which mistake this is.</summary>
    public SpecValidationErrorCode Code { get; }

    /// <summary>
    ///     Gets the ID of the rule or scope the mistake is about, or null when it is about the spec as a
    ///     whole — a duplicate layer name, or a mistake in a layer's definition.
    /// </summary>
    public string? RuleId { get; }

    /// <summary>
    ///     Gets where in the spec source the offending call was written, or null when the mistake is about
    ///     the spec as a whole and when no position was captured. Where it is present it is already rendered
    ///     at the front of <see cref="Message" />.
    /// </summary>
    public SpecSourceLocation? Location { get; }

    /// <summary>
    ///     Gets the message to print, opening with <c>file:line</c> when a position was captured and with
    ///     the diagnostic itself otherwise.
    /// </summary>
    public string Message { get; }
}
