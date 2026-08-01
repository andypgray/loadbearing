namespace Zphil.LoadBearing.Model;

/// <summary>
///     Clause/trailer stage for a quarantined scope. Each clause appends so repeats are detectable
///     (§8 item 6); <see cref="BoundaryOnlyVia" /> also bumps a call counter so a hermetic quarantine
///     (never called) is distinguished from an empty-boundary error (called with zero types).
/// </summary>
internal sealed class QuarantinedScopeBuilder(ScopeRegistration registration) : IQuarantinedScope
{
    public IQuarantinedScope BoundaryOnlyVia(params Type[] boundary)
    {
        registration.BoundaryOnlyViaCount++;
        foreach (Type type in boundary) registration.Boundary.Add(type);

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