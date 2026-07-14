namespace Zphil.LoadBearing.Model;

/// <summary>
///     Clause/trailer stage for a frozen scope. Each clause appends so repeats are detectable
///     (§8 item 6); <see cref="BoundaryOnlyVia" /> also bumps a call counter so a hermetic freeze
///     (never called) is distinguished from an empty-boundary error (called with zero types).
/// </summary>
internal sealed class FrozenScopeBuilder(ScopeRegistration registration) : IFrozenScope
{
    public IFrozenScope BoundaryOnlyVia(params Type[] boundary)
    {
        registration.BoundaryOnlyViaCount++;
        foreach (Type type in boundary) registration.Boundary.Add(type);

        return this;
    }

    public IFrozenScope Dragons(string prose)
    {
        registration.Dragons.Add(prose);
        return this;
    }

    public IFrozenScope DragonsDoc(string path)
    {
        registration.DragonsDocs.Add(path);
        return this;
    }

    public IFrozenScope Baseline(string path)
    {
        registration.Baselines.Add(path);
        return this;
    }

    public IFrozenScope Because(string because)
    {
        registration.Becauses.Add(because);
        return this;
    }
}