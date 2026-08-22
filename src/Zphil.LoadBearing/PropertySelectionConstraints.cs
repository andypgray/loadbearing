using Zphil.LoadBearing.Fluent;
using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing;

/// <summary>
///     The properties-only member modal verb (GRAMMAR §5.7) as an extension that turns a
///     <see cref="PropertySelection" /> into a terminal <see cref="Constraint" />.
/// </summary>
/// <remarks>
///     Like <c>.Returning</c> on <see cref="MethodSelection" />, it binds by receiver type to
///     <see cref="PropertySelection" /> — the <c>.Properties</c> projection's selection — so it is
///     uncompilable off <c>.Methods</c>/<c>.Fields</c>/<c>.Events</c>/<c>.Members</c> (properties-only by
///     construction, GRAMMAR §3.2).
/// </remarks>
public static class PropertySelectionConstraints
{
    /// <summary>
    ///     The subject properties must declare no setter accessor (GRAMMAR §5.7). Strict: an
    ///     <c>init</c>-only setter is still a setter, so <c>{ get; init; }</c> fails. Accessibility-blind
    ///     — a <c>private set</c> fails too.
    /// </summary>
    public static Constraint MustBeGetOnly(this PropertySelection subject)
    {
        return new MemberMustBeGetOnlyConstraint(Guard.NotNull(subject, nameof(subject)));
    }
}
