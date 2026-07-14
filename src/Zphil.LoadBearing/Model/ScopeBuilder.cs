using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Model;

/// <summary>Posture stage for a scope anchor: <c>Freeze</c> records the frozen selection (GRAMMAR §3.2).</summary>
internal sealed class ScopeBuilder(ScopeRegistration registration) : IScopeBuilder
{
    public IFrozenScope Freeze(Selection selection)
    {
        registration.Frozen = Guard.NotNull(selection, nameof(selection));
        return new FrozenScopeBuilder(registration);
    }
}