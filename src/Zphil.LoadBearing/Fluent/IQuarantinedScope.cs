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
    ///     The sanctioned surface — the only selections that may reference into the quarantined scope.
    ///     Several are the union <c>arch.AnyOf</c> would mint, and the formula holds whether they lie
    ///     inside or outside the quarantined selection (GRAMMAR §7). This is the no-load spelling: a
    ///     facade the spec assembly cannot compile against is named by
    ///     <c>arch.Types.Named("BillingFacade")</c> rather than by <c>typeof</c>.
    /// </summary>
    IQuarantinedScope BoundaryOnlyVia(Selection first, params Selection[] more);

    /// <summary>
    ///     The sanctioned surface as types — <c>≡ BoundaryOnlyVia(arch.Type(a), arch.Type(b), …)</c>, the
    ///     same sugar the dependency verbs carry (GRAMMAR §3.3): identical model, identical prose. Omit the
    ///     call entirely for a hermetic quarantine; a zero-argument call is a validation error with a hint
    ///     (§8 item 8), which is why this overload alone keeps plain <c>params</c> — the <c>Selection</c>
    ///     form's <c>(first, more)</c> shape is what leaves <c>BoundaryOnlyVia()</c> binding here uniquely
    ///     instead of ambiguously.
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
