using Zphil.LoadBearing.Fluent;

namespace Zphil.LoadBearing;

/// <summary>
///     The stage after <c>arch.Scope(id)</c>: a scope takes exactly one posture verb (GRAMMAR §3.2).
///     <c>Quarantine</c> is a containment law plus a tripwire plus dragons — nothing new outside the
///     sanctioned surface may reference the scope. <c>Caution</c> is the tripwire plus dragons alone —
///     new references are welcome; the weirdness inside is what wants reading first. A dangling
///     <c>arch.Scope("x")</c> with no posture verb is caught by validation (§8 item 2).
/// </summary>
public interface IScopeBuilder
{
    /// <summary>Quarantines a selection; desugars into containment + tripwire rule nodes (GRAMMAR §7).</summary>
    IQuarantinedScope Quarantine(Selection selection);

    /// <summary>Cautions a selection; desugars into a tripwire rule node alone (GRAMMAR §7).</summary>
    ICautionedScope Caution(Selection selection);
}
