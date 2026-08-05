namespace Zphil.LoadBearing;

/// <summary>
///     The stage after <c>arch.Scope(id)</c>: a scope's only posture is <c>Quarantine</c> (GRAMMAR
///     §3.2). A dangling <c>arch.Scope("x")</c> with no <c>Quarantine</c> is caught by validation
///     (§8 item 2).
/// </summary>
public interface IScopeBuilder
{
    /// <summary>Quarantines a selection; desugars into containment + tripwire rule nodes (GRAMMAR §7).</summary>
    IQuarantinedScope Quarantine(Selection selection);
}
