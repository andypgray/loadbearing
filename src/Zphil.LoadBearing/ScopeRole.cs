using Zphil.LoadBearing.Hosting;

namespace Zphil.LoadBearing;

/// <summary>
///     A scope is checked as two rules, and this says which of them an <see cref="ArchRule" /> is. A
///     quarantined scope has both, a cautioned scope the tripwire alone, and their rule IDs are the
///     scope's own ID followed by <c>/containment</c> and <c>/tripwire</c>.
/// </summary>
public enum ScopeRole
{
    /// <summary>
    ///     The boundary rule: a reference into the scope from a type outside it that is not part of the
    ///     sanctioned surface fails the check, unless the scope's baseline records it, in which case it is
    ///     grandfathered.
    /// </summary>
    Containment,

    /// <summary>
    ///     The warning rule: a check run with a diff base (<c>check --diff-base &lt;ref&gt;</c>) warns
    ///     about every changed file that declares a type inside the scope, and points at the dragons text.
    ///     It never fails the check.
    /// </summary>
    Tripwire
}
