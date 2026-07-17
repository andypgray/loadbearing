namespace Meridian.Quoting.Domain;

/// <summary>
///     The port for reading the current instant. Injecting it makes "now" an input, so pricing
///     and expiry can be exercised at a fixed moment in tests.
/// </summary>
public interface IClock
{
    /// <summary>The current UTC instant.</summary>
    DateTime UtcNow { get; }
}