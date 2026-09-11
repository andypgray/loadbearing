namespace Zphil.LoadBearing.Fluent;

/// <summary>
///     The calls available after <c>Caution</c>: at least one of <see cref="Dragons" /> and
///     <see cref="DragonsDoc" />, and <see cref="Because" />, which is required; each at most once. A
///     caution has no boundary, so there is no <c>BoundaryOnlyVia</c>; nothing to grandfather, so no
///     <c>Baseline</c>; and no red state, so no <c>Fix</c>. Every prose value is a single non-blank
///     line; anything else is reported when the spec is loaded.
/// </summary>
public interface ICautionedScope
{
    /// <summary>
    ///     Describes the load-bearing strangeness inside the scope, in one line of prose: what an editor
    ///     must know before touching it. Rendered into the generated context for the scope's directory
    ///     and printed when a check warns about an edit inside the scope. At least one of <c>Dragons</c>
    ///     and <see cref="DragonsDoc" /> is required.
    /// </summary>
    ICautionedScope Dragons(string prose);

    /// <summary>
    ///     Links a longer document about the scope, for what one line cannot hold. The path is rendered
    ///     as written into the generated context and the check report; nothing resolves or reads it, so
    ///     give one a reader of those can follow. At least one of <see cref="Dragons" /> and
    ///     <c>DragonsDoc</c> is required.
    /// </summary>
    ICautionedScope DragonsDoc(string path);

    /// <summary>
    ///     States why the scope exists, in one line of prose. Required: a scope without it is reported
    ///     when the spec is loaded. Rendered into the generated agent context.
    /// </summary>
    ICautionedScope Because(string because);
}
