using Zphil.LoadBearing.Fluent;
using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing;

/// <summary>
///     The fields-only member modal verb (GRAMMAR §5.7) as an extension that turns a
///     <see cref="FieldSelection" /> into a terminal <see cref="Constraint" />.
/// </summary>
/// <remarks>
///     Like <c>.Returning</c> on <see cref="MethodSelection" />, it binds by receiver type to
///     <see cref="FieldSelection" /> — the <c>.Fields</c> projection's selection — so it is uncompilable
///     off <c>.Methods</c>/<c>.Properties</c>/<c>.Events</c>/<c>.Members</c> (fields-only by
///     construction, GRAMMAR §3.2).
/// </remarks>
public static class FieldSelectionConstraints
{
    /// <summary>
    ///     The subject fields must be declared <c>readonly</c> (GRAMMAR §5.7). A <c>const</c> field
    ///     satisfies the verb — const is readonly's superset, so redding one would demand something weaker
    ///     than what is already there.
    /// </summary>
    public static Constraint MustBeReadonly(this FieldSelection subject)
    {
        return new MemberMustBeReadonlyConstraint(Guard.NotNull(subject, nameof(subject)));
    }
}
