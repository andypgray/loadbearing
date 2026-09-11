namespace Zphil.LoadBearing.Model;

/// <summary>
///     Mutable backing state for a scope anchor. A null <see cref="Posture" /> means no posture verb was
///     called (dangling, §8 item 2) — never a null <see cref="Scoped" />, which the two verbs set
///     together with the posture. <see cref="BoundaryOnlyViaCount" /> distinguishes a hermetic quarantine
///     (never called) from an empty-boundary error (called with zero types, §8 item 8 — reachable through
///     the <c>Type</c> overload alone) and a repeated call (§8 item 6).
/// </summary>
internal sealed class ScopeRegistration(string id) : Registration(id)
{
    /// <summary>The posture, or null when no posture verb was called.</summary>
    internal Posture? Posture { get; set; }

    /// <summary>The scoped selection, or null when no posture verb was called.</summary>
    internal Selection? Scoped { get; set; }

    /// <summary>
    ///     How many times a posture verb was assigned. The stage machine (§3.2) forbids a fluent double-call,
    ///     but a stored <c>IScopeBuilder</c> reference is mutable, so <c>Quarantine</c>/<c>Caution</c> called
    ///     twice on it silently overwrites the scoped selection and its posture; a count &gt; 1 is the
    ///     repeated-posture error (§8 item 17).
    /// </summary>
    internal int PostureCount { get; set; }

    /// <summary>The accumulated boundary selections across every <c>BoundaryOnlyVia</c> call.</summary>
    internal List<Selection> Boundary { get; } = [];

    /// <summary>How many times <c>BoundaryOnlyVia</c> was called (0 = hermetic quarantine).</summary>
    internal int BoundaryOnlyViaCount { get; set; }

    /// <summary>Every <c>Baseline</c> supplied (at most one is valid).</summary>
    internal List<string> Baselines { get; } = [];

    /// <summary>Every <c>Dragons</c> prose supplied (at most one is valid).</summary>
    internal List<string> Dragons { get; } = [];

    /// <summary>Every <c>DragonsDoc</c> path supplied (at most one is valid).</summary>
    internal List<string> DragonsDocs { get; } = [];

    /// <summary>Every <c>Because</c> supplied (exactly one is valid).</summary>
    internal List<string> Becauses { get; } = [];
}
