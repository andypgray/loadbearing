using Zphil.LoadBearing.Hosting;

namespace Zphil.LoadBearing;

/// <summary>Which half of a desugared Quarantine scope an <see cref="ArchRule" /> is (GRAMMAR §7).</summary>
public enum QuarantineRole
{
    /// <summary>The hard-red boundary rule: nothing new may reference the scope except via the surface.</summary>
    Containment,

    /// <summary>The warning-severity, diff-aware touch check (<c>check --diff-base &lt;ref&gt;</c>).</summary>
    Tripwire
}
