using Zphil.LoadBearing.Hosting;

namespace Zphil.LoadBearing.Validation;

/// <summary>
///     Thrown when a spec is loaded and something in it is wrong: <see cref="ArchModelBuilder" /> raises
///     it instead of returning a model. <see cref="Errors" /> carries every mistake found in that one
///     pass rather than only the first, and the message is those errors one per line.
/// </summary>
public sealed class SpecValidationException : Exception
{
    internal SpecValidationException(IReadOnlyList<SpecValidationError> errors)
        : base(string.Join("\n", errors.Select(e => e.Message)))
    {
        Errors = errors;
    }

    /// <summary>
    ///     Gets every mistake found, in the order the checks ran. Each names its kind, the rule or scope it
    ///     is about, and where in the spec source it was written.
    /// </summary>
    public IReadOnlyList<SpecValidationError> Errors { get; }
}
