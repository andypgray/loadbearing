namespace Zphil.LoadBearing.Validation;

/// <summary>
///     Thrown by <see cref="ArchModelBuilder" /> when a spec fails validation. Carries every
///     <see cref="SpecValidationError" /> from a single pass — a deliberate divergence from EF
///     Core's fail-fast validator so an agent fixing a spec sees all problems at once (GRAMMAR §8).
/// </summary>
public sealed class SpecValidationException : Exception
{
    internal SpecValidationException(IReadOnlyList<SpecValidationError> errors)
        : base(string.Join("\n", errors.Select(e => e.Message)))
    {
        Errors = errors;
    }

    /// <summary>Every validation error found, in the order the checks ran.</summary>
    public IReadOnlyList<SpecValidationError> Errors { get; }
}