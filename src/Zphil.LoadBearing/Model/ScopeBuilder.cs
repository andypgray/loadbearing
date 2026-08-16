using Zphil.LoadBearing.Fluent;
using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Model;

/// <summary>Posture stage for a scope anchor: <c>Quarantine</c> records the quarantined selection (GRAMMAR §3.2).</summary>
internal sealed class ScopeBuilder(ScopeRegistration registration) : IScopeBuilder
{
    public IQuarantinedScope Quarantine(Selection selection)
    {
        registration.QuarantineCount++;
        registration.Quarantined = Guard.NotNull(selection, nameof(selection));
        return new QuarantinedScopeBuilder(registration);
    }
}
