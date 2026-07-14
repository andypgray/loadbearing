namespace Zphil.LoadBearing.Validation;

/// <summary>
///     A single spec-build validation failure (GRAMMAR §8). Pinned primarily by <see cref="Code" />
///     plus <see cref="RuleId" />, with one representative <see cref="Message" /> per code.
/// </summary>
public sealed class SpecValidationError
{
    internal SpecValidationError(SpecValidationErrorCode code, string? ruleId, string message)
    {
        Code = code;
        RuleId = ruleId;
        Message = message;
    }

    /// <summary>The catalog code.</summary>
    public SpecValidationErrorCode Code { get; }

    /// <summary>The offending rule or scope ID, or null for spec-wide errors (e.g. a duplicate layer name).</summary>
    public string? RuleId { get; }

    /// <summary>The human-readable diagnostic.</summary>
    public string Message { get; }
}