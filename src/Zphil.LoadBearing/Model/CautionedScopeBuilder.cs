using Zphil.LoadBearing.Fluent;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     Clause/trailer stage for a cautioned scope. Each clause appends so repeats are detectable
///     (§8 item 6). The stage carries only the three clauses a caution can hold — there is no boundary
///     or baseline to record, so the counters <see cref="QuarantinedScopeBuilder" /> maintains for those
///     stay untouched and their validation arms are unreachable from here.
/// </summary>
internal sealed class CautionedScopeBuilder(ScopeRegistration registration) : ICautionedScope
{
    public ICautionedScope Dragons(string prose)
    {
        registration.Dragons.Add(prose);
        return this;
    }

    public ICautionedScope DragonsDoc(string path)
    {
        registration.DragonsDocs.Add(path);
        return this;
    }

    public ICautionedScope Because(string because)
    {
        registration.Becauses.Add(because);
        return this;
    }
}
