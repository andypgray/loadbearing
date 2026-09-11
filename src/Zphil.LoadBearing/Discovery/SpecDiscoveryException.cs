namespace Zphil.LoadBearing.Discovery;

/// <summary>
///     Thrown by <see cref="SpecDiscovery.FindSpecs" /> when an assembly declares no
///     <see cref="IArchitectureSpec" /> class. Finding none is an error, never an empty result.
/// </summary>
public sealed class SpecDiscoveryException : Exception
{
    internal SpecDiscoveryException(string message)
        : base(message)
    {
    }
}
