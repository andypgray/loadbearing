using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing;

/// <summary>
///     The methods-only member modal verb (GRAMMAR §5.7) as an extension that turns a
///     <see cref="MethodSelection" /> into a terminal <see cref="Constraint" />. Like <c>.Returning</c>,
///     it binds by receiver type to <see cref="MethodSelection" /> — the <c>.Methods</c> projection's
///     selection — so it is uncompilable off <c>.Properties</c>/<c>.Fields</c>/<c>.Events</c>/
///     <c>.Members</c>, which are plain <see cref="MemberSelection" /> (methods-only by construction,
///     GRAMMAR §3.2). Single-<see cref="Type" /> arity is deliberate: a multi-type parameter list is
///     all-vs-any ambiguous, so there is no <c>params</c> overload.
/// </summary>
public static class MethodSelectionConstraints
{
    /// <summary>
    ///     The subject methods must accept a parameter of the given type, matched definition-level
    ///     (GRAMMAR §4.6): a non-generic anchor (<c>typeof(CancellationToken)</c>) matches exactly, an
    ///     open-generic anchor (<c>typeof(IProgress&lt;&gt;)</c>) matches any construction. A
    ///     closed-generic anchor is refused at spec build (GRAMMAR §8 item 20).
    /// </summary>
    public static Constraint MustAcceptParameter(this MethodSelection subject, Type parameterType)
    {
        return new MemberMustAcceptParameterConstraint(
            Guard.NotNull(subject, nameof(subject)),
            Guard.NotNull(parameterType, nameof(parameterType)));
    }
}