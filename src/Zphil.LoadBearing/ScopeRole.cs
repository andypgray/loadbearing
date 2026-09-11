using Zphil.LoadBearing.Hosting;

namespace Zphil.LoadBearing;

/// <summary>
///     Which child of a desugared scope an <see cref="ArchRule" /> is (GRAMMAR §7). A quarantine mints
///     both; a caution mints the tripwire alone.
/// </summary>
public enum ScopeRole
{
    /// <summary>The hard-red boundary rule: nothing new may reference the scope except via the surface.</summary>
    Containment,

    /// <summary>The warning-severity, diff-aware touch check (<c>check --diff-base &lt;ref&gt;</c>).</summary>
    Tripwire
}
