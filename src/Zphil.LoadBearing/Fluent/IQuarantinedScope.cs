namespace Zphil.LoadBearing.Fluent;

/// <summary>
///     Clauses and trailers available on a quarantined scope (GRAMMAR §3.2). <c>Because</c> is required
///     (§8 item 3) and at least one of <c>Dragons</c>/<c>DragonsDoc</c> is required (§8 item 4).
///     The containment <c>Fix</c> is auto-derived from <c>BoundaryOnlyVia</c> (GRAMMAR §5.5), so
///     there is deliberately no <c>Fix</c> clause on this stage.
/// </summary>
public interface IQuarantinedScope
{
    /// <summary>
    ///     The sanctioned surface — the only types that may reference into the quarantined scope. Omit
    ///     the call entirely for a hermetic quarantine; a zero-argument call is a validation error with
    ///     a hint (§8 item 8), hence plain <c>params</c> rather than the <c>(first, more)</c> shape.
    /// </summary>
    IQuarantinedScope BoundaryOnlyVia(params Type[] boundary);

    /// <summary>Load-bearing-weirdness prose rendered into scoped context.</summary>
    IQuarantinedScope Dragons(string prose);

    /// <summary>A linked long-form dragons document (a file path).</summary>
    IQuarantinedScope DragonsDoc(string path);

    /// <summary>The ratcheted grandfather store for existing inbound references (GRAMMAR §7).</summary>
    IQuarantinedScope Baseline(string path);

    /// <summary>The rationale — required.</summary>
    IQuarantinedScope Because(string because);
}
