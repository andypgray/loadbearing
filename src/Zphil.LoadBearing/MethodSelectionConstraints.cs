using Zphil.LoadBearing.Fluent;
using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing;

/// <summary>
///     The methods-only member modal verb (GRAMMAR §5.7) as an extension that turns a
///     <see cref="MethodSelection" /> into a terminal <see cref="Constraint" />.
/// </summary>
/// <remarks>
///     Like <c>.Returning</c>, it binds by receiver type to <see cref="MethodSelection" /> — the
///     <c>.Methods</c> projection's selection — so it is uncompilable off <c>.Properties</c> and
///     <c>.Fields</c>, which mint kind-scoped selections of their own, and off <c>.Events</c> and
///     <c>.Members</c>, which are plain <see cref="MemberSelection" /> (methods-only by construction,
///     GRAMMAR §3.2).
///     Single-anchor arity is deliberate: a multi-type parameter list is all-vs-any
///     ambiguous, so there is no <c>params</c> overload.
/// </remarks>
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
        TypeAnchor anchor = TypeAnchor.FromType(Guard.NotNull(parameterType, nameof(parameterType)));
        return new MemberMustAcceptParameterConstraint(Guard.NotNull(subject, nameof(subject)), anchor);
    }

    /// <summary>
    ///     The subject methods must accept a parameter of the type named by string — the escape hatch for
    ///     a parameter type the spec project cannot compile against, so it need not take a package
    ///     reference just to write the <c>typeof</c>. <paramref name="parameterTypeFullName" /> is the
    ///     parameter type <em>definition</em>'s fully-qualified name as a report prints it, declared
    ///     type-parameter names for a generic (<c>"System.IProgress&lt;T&gt;"</c>); it matches any
    ///     construction of that definition, and a constructed spelling matches nothing. Prefer
    ///     <see cref="MustAcceptParameter(MethodSelection,Type)" /> whenever the type is referenceable —
    ///     the compiler checks a <c>typeof</c>, and nothing checks a string.
    /// </summary>
    public static Constraint MustAcceptParameter(this MethodSelection subject, string parameterTypeFullName)
    {
        TypeAnchor anchor = TypeAnchor.FromName(Guard.NotNull(parameterTypeFullName, nameof(parameterTypeFullName)));
        return new MemberMustAcceptParameterConstraint(Guard.NotNull(subject, nameof(subject)), anchor);
    }

    /// <summary>
    ///     The subject methods must accept a parameter of type <typeparamref name="T" /> —
    ///     <c>≡ MustAcceptParameter(typeof(T))</c>; an open generic stays <c>typeof</c>. Unconstrained, so
    ///     a value type (<c>MustAcceptParameter&lt;CancellationToken&gt;()</c>, the flagship anchor) reads
    ///     as naturally as a reference one.
    /// </summary>
    public static Constraint MustAcceptParameter<T>(this MethodSelection subject)
    {
        return subject.MustAcceptParameter(typeof(T));
    }
}
