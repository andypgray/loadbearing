namespace Zphil.LoadBearing.Model;

/// <summary>
///     Mutable backing state for a scope anchor. A null <see cref="Frozen" /> means <c>Freeze</c>
///     was never called (dangling, §8 item 2). <see cref="BoundaryOnlyViaCount" /> distinguishes a
///     hermetic freeze (never called) from an empty-boundary error (called with zero types, §8
///     item 8) and a repeated call (§8 item 6).
/// </summary>
internal sealed class ScopeRegistration(string id) : Registration(id)
{
    /// <summary>The frozen selection, or null when <c>Freeze</c> was not called.</summary>
    internal Selection? Frozen { get; set; }

    /// <summary>
    ///     How many times <c>Freeze</c> was called. The stage machine (§3.2) forbids a fluent double-call,
    ///     but a stored <c>IScopeBuilder</c> reference is mutable, so a second <c>Freeze</c> silently
    ///     overwrites the frozen selection; a count &gt; 1 is the repeated-posture error (§8 item 17).
    /// </summary>
    internal int FreezeCount { get; set; }

    /// <summary>The accumulated boundary types across every <c>BoundaryOnlyVia</c> call.</summary>
    internal List<Type> Boundary { get; } = [];

    /// <summary>How many times <c>BoundaryOnlyVia</c> was called (0 = hermetic freeze).</summary>
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