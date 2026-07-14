namespace Zphil.LoadBearing.Discovery;

/// <summary>
///     Thrown when <see cref="SpecDiscovery.FindSpecs" /> finds no specs in an assembly. Specs are
///     law; a zero result is loud, never a silent skip (GRAMMAR §9 divergence from EF's undefined
///     order and silent skips).
/// </summary>
public sealed class SpecDiscoveryException : Exception
{
    internal SpecDiscoveryException(string message)
        : base(message)
    {
    }
}