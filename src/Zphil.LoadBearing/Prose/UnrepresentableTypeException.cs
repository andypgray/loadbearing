namespace Zphil.LoadBearing.Prose;

/// <summary>
///     Thrown by <see cref="TypeName.FullDisplay" /> when a reflection <see cref="Type" /> has no
///     extraction-format analog: pointers, by-refs, and partially-open constructions (some type
///     arguments bound, some free).
/// </summary>
internal sealed class UnrepresentableTypeException(Type type)
    : Exception($"Type '{type}' has no source-level display form the checker can match against.")
{
    /// <summary>The offending type.</summary>
    internal Type Type { get; } = type;
}
