namespace Zphil.LoadBearing.Fluent;

/// <summary>
///     The calls available after <c>Quarantine</c>: the optional <c>BoundaryOnlyVia</c> and
///     <see cref="Baseline" />, at least one of <see cref="Dragons" /> and <see cref="DragonsDoc" />,
///     and <see cref="Because" />, which is required; in any order and each at most once. There is no
///     <c>Fix</c>: the remedy for a containment violation is always to go through the boundary, and
///     the report says so itself. Every prose value is a single non-blank line; anything else is
///     reported when the spec is loaded.
/// </summary>
public interface IQuarantinedScope
{
    /// <summary>
    ///     Names the sanctioned surface: the only types outside the quarantine that may reference into it,
    ///     such as <c>BoundaryOnlyVia(arch.Types.Named("IBillingFacade", "BillingFacade"))</c>. Any
    ///     selection works, whether it lies inside or outside the quarantined selection, and several are
    ///     combined into one set. Naming the facade with <c>Named</c> reaches an <c>internal</c> type, or
    ///     one in a project the spec does not reference, without the spec compiling against it. Omit the
    ///     call entirely for a hermetic quarantine, which nothing outside may reference. At most once.
    /// </summary>
    IQuarantinedScope BoundaryOnlyVia(Selection first, params Selection[] more);

    /// <summary>
    ///     Names the sanctioned surface as types: the only types outside the quarantine that may reference
    ///     into it, such as <c>BoundaryOnlyVia(typeof(IBillingFacade), typeof(BillingFacade))</c>. List
    ///     the facade's implementation type beside its interface, or the composition root's registration
    ///     of the concrete type fails on day one. Calling this with no arguments is reported when the spec
    ///     is loaded; omit the call entirely for a hermetic quarantine, which nothing outside may
    ///     reference. At most once.
    /// </summary>
    IQuarantinedScope BoundaryOnlyVia(params Type[] boundary);

    /// <summary>
    ///     Describes the load-bearing strangeness inside the scope, in one line of prose: what an editor
    ///     must know before touching it. Rendered into the generated context for the scope's directory
    ///     and printed when a check warns about an edit inside the scope. At least one of <c>Dragons</c>
    ///     and <see cref="DragonsDoc" /> is required.
    /// </summary>
    IQuarantinedScope Dragons(string prose);

    /// <summary>
    ///     Links a longer document about the scope, for what one line cannot hold. The path is rendered
    ///     as written into the generated context and the check report; nothing resolves or reads it, so
    ///     give one a reader of those can follow. At least one of <see cref="Dragons" /> and
    ///     <c>DragonsDoc</c> is required.
    /// </summary>
    IQuarantinedScope DragonsDoc(string path);

    /// <summary>
    ///     Sets the file that records the references into the scope that existed when it was declared,
    ///     captured with the CLI's <c>baseline</c> verb; each is reported as grandfathered rather than
    ///     failing the check. Optional: when omitted the path is
    ///     <c>arch/baselines/{scope-id}/containment.json</c>. A relative path resolves against the
    ///     solution directory. Until a baseline is captured, every reference into the scope from outside
    ///     the sanctioned surface fails the check.
    /// </summary>
    IQuarantinedScope Baseline(string path);

    /// <summary>
    ///     States why the scope exists, in one line of prose. Required: a scope without it is reported
    ///     when the spec is loaded. Rendered into the generated agent context and echoed with every
    ///     containment violation.
    /// </summary>
    IQuarantinedScope Because(string because);
}
