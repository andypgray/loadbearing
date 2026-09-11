using Zphil.LoadBearing.Fluent;

namespace Zphil.LoadBearing;

/// <summary>
///     The step after <c>arch.Scope(id)</c>: a scope takes exactly one posture, so the only members
///     here are <see cref="Quarantine" /> and <see cref="Caution" />. A scope left without either is
///     reported when the spec is loaded.
/// </summary>
public interface IScopeBuilder
{
    /// <summary>
    ///     Quarantines the selection: nothing outside it may reference into it, and once a baseline is
    ///     captured with the CLI's <c>baseline</c> verb the references that existed then are reported as
    ///     grandfathered instead of failing the check. Follow with <c>BoundaryOnlyVia</c> to name the
    ///     types that may keep referencing it (omit it for a hermetic quarantine), <c>Dragons</c> or
    ///     <c>DragonsDoc</c> to say what is strange inside (at least one is required), and <c>Because</c>,
    ///     which is required. A check run with a diff base also warns about every changed file that
    ///     declares a quarantined type, asking whether the task needs to be in there.
    /// </summary>
    IQuarantinedScope Quarantine(Selection selection);

    /// <summary>
    ///     Marks the selection as territory to read about before editing, with no boundary: references
    ///     into it stay legal and nothing about it ever fails the check. A check run with a diff base warns
    ///     about every changed file that declares one of its types, pointing at the dragons text. Follow
    ///     with <c>Dragons</c> or <c>DragonsDoc</c> (at least one is required) and <c>Because</c>, which is
    ///     required.
    /// </summary>
    ICautionedScope Caution(Selection selection);
}
