namespace Zphil.LoadBearing.Fluent;

/// <summary>
///     Clauses and trailers available on a cautioned scope (GRAMMAR §3.2). <c>Because</c> is required
///     (§8 item 3) and at least one of <c>Dragons</c>/<c>DragonsDoc</c> is required (§8 item 4).
///     There is deliberately no <c>BoundaryOnlyVia</c>, <c>Baseline</c> or <c>Fix</c> clause: a caution
///     declares no containment law, so it has no sanctioned surface to name, no inbound references to
///     grandfather, and no red state to remedy.
/// </summary>
public interface ICautionedScope
{
    /// <summary>Load-bearing-weirdness prose rendered into scoped context.</summary>
    ICautionedScope Dragons(string prose);

    /// <summary>A linked long-form dragons document (a file path).</summary>
    ICautionedScope DragonsDoc(string path);

    /// <summary>The rationale — required.</summary>
    ICautionedScope Because(string because);
}
