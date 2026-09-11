using Zphil.LoadBearing.Fluent;
using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing;

/// <summary>
///     The verb available on a method selection alone — the <c>Methods</c> projection of a
///     <see cref="Selection" />. <c>MustAcceptParameter</c> asks a question only a method can answer,
///     so it does not compile on a property, field, event or plain member selection, and it takes one
///     type rather than a list: a second required parameter is a second rule.
/// </summary>
// Receiver-typed to MethodSelection exactly like .Returning, so the verb is uncompilable off
// .Properties, .Fields, .Events and .Members — no validation rule to write and no runtime refusal to
// render (GRAMMAR §5.7).
public static class MethodSelectionConstraints
{
    /// <summary>
    ///     States that every selected method must declare a parameter of the given type, such as
    ///     <c>arch.Types.Methods.WithSuffix("Async").MustAcceptParameter(typeof(CancellationToken))</c>,
    ///     and returns the <see cref="Constraint" /> to hand to <c>Enforce</c> or <c>Migrate</c>. A method
    ///     passes when any one of its parameters has that type and fails the check otherwise. Matching is
    ///     by type definition: a non-generic type matches exactly, and an open generic such as
    ///     <c>typeof(IProgress&lt;&gt;)</c> matches every construction of it. A constructed generic such
    ///     as <c>typeof(IProgress&lt;int&gt;)</c> is reported when the spec is loaded, naming the open
    ///     definition to use instead. The declaration decides the rest: a parameter with a default value
    ///     counts, an extension method's <c>this</c> parameter counts, <c>ref</c>, <c>in</c> and
    ///     <c>out</c> change nothing, and neither <c>params CancellationToken[]</c> nor
    ///     <c>CancellationToken?</c> satisfies a <c>CancellationToken</c> parameter, an array and a
    ///     nullable being types of their own.
    /// </summary>
    public static Constraint MustAcceptParameter(this MethodSelection subject, Type parameterType)
    {
        TypeAnchor anchor = TypeAnchor.FromType(Guard.NotNull(parameterType, nameof(parameterType)));
        return new MemberMustAcceptParameterConstraint(Guard.NotNull(subject, nameof(subject)), anchor);
    }

    /// <summary>
    ///     States that every selected method must declare a parameter of the type with this name — the form to
    ///     use for a parameter type the spec project cannot compile against, so it need not take a package
    ///     reference just to write the <c>typeof</c>. <paramref name="parameterTypeFullName" /> is the
    ///     type definition's fully-qualified name as a report prints it, declared type-parameter names
    ///     included for a generic (<c>"System.Threading.CancellationToken"</c>,
    ///     <c>"System.IProgress&lt;T&gt;"</c>); it accepts every construction of that definition, and a
    ///     constructed spelling matches nothing. A method passes when any one of its parameters has that
    ///     type and fails the check otherwise. Blankness is the only thing checked, and a blank name is
    ///     reported when the spec is loaded: a misspelt name is a legal name no parameter can have, so
    ///     every selected method then fails the check. Prefer the <see cref="System.Type" /> overload
    ///     whenever the type is referenceable, because the compiler checks a <c>typeof</c> and nothing
    ///     checks a string.
    /// </summary>
    public static Constraint MustAcceptParameter(this MethodSelection subject, string parameterTypeFullName)
    {
        TypeAnchor anchor = TypeAnchor.FromName(Guard.NotNull(parameterTypeFullName, nameof(parameterTypeFullName)));
        return new MemberMustAcceptParameterConstraint(Guard.NotNull(subject, nameof(subject)), anchor);
    }

    /// <summary>
    ///     States that every selected method must declare a parameter of type <typeparamref name="T" />, such
    ///     as <c>arch.Types.Methods.MustAcceptParameter&lt;CancellationToken&gt;()</c>. A method passes
    ///     when any one of its parameters has that type and fails the check otherwise; a value type reads
    ///     as naturally here as a reference type, nothing being required of <typeparamref name="T" />. An
    ///     open generic has no type-argument form: for <c>IProgress&lt;&gt;</c> use the
    ///     <see cref="System.Type" /> overload and pass <c>typeof(IProgress&lt;&gt;)</c>, which accepts
    ///     every construction of it.
    /// </summary>
    public static Constraint MustAcceptParameter<T>(this MethodSelection subject)
    {
        return subject.MustAcceptParameter(typeof(T));
    }
}
