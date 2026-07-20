namespace Zphil.LoadBearing.Validation;

/// <summary>
///     A single spec-build validation failure (GRAMMAR §8). Pinned primarily by <see cref="Code" />
///     plus <see cref="RuleId" />, with one representative <see cref="Message" /> per code.
/// </summary>
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

    /// <summary>The catalog code.</summary>
    public SpecValidationErrorCode Code { get; }

    /// <summary>The offending rule or scope ID, or null for spec-wide errors (e.g. a duplicate layer name).</summary>
    public string? RuleId { get; }

    /// <summary>
    ///     The spec-source position of the offending anchor (GRAMMAR §8), or null for a spec-wide error
    ///     (duplicate ID, layer errors) or an error whose location was never captured. When present it is
    ///     already rendered into the front of <see cref="Message" />.
    /// </summary>
    public SpecSourceLocation? Location { get; }

    /// <summary>The human-readable diagnostic, prefixed with <c>file:line</c> when a location was captured.</summary>
    public string Message { get; }
}