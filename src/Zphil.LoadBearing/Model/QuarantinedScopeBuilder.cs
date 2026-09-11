using Zphil.LoadBearing.Fluent;
using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     Clause/trailer stage for a quarantined scope. Each clause appends so repeats are detectable
///     (§8 item 6); <see cref="BoundaryOnlyVia(Type[])" /> also bumps a call counter so a hermetic
///     quarantine (never called) is distinguished from an empty-boundary error (called with zero types).
/// </summary>
internal sealed class QuarantinedScopeBuilder(ScopeRegistration registration) : IQuarantinedScope
{
    public IQuarantinedScope BoundaryOnlyVia(Selection first, params Selection[] more)
    {
        Guard.NotNull(more, nameof(more));
        registration.BoundaryOnlyViaCount++;
        registration.Boundary.AddRange(OperandList.OneOrMore(first, more, facade => facade));

        return this;
    }

    // The stage machine (§3.2) reaches this clause only after Quarantine ran, so the quarantined
    // selection — and with it the Arch the wrapped types must belong to — is always there.
    public IQuarantinedScope BoundaryOnlyVia(params Type[] boundary)
    {
        Guard.NotNull(boundary, nameof(boundary));
        Arch owner = registration.Scoped!.Owner;
        registration.BoundaryOnlyViaCount++;
        foreach (Type type in boundary) registration.Boundary.Add(owner.Type(Guard.NotNull(type, nameof(boundary))));

        return this;
    }

    public IQuarantinedScope Dragons(string prose)
    {
        registration.Dragons.Add(prose);
        return this;
    }

    public IQuarantinedScope DragonsDoc(string path)
    {
        registration.DragonsDocs.Add(path);
        return this;
    }

    public IQuarantinedScope Baseline(string path)
    {
        registration.Baselines.Add(path);
        return this;
    }

    public IQuarantinedScope Because(string because)
    {
        registration.Becauses.Add(because);
        return this;
    }
}
