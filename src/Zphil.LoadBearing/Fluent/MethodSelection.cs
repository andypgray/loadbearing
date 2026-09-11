using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing.Fluent;

/// <summary>
///     The methods the selected types declare, reached with <c>.Methods</c> on a
///     <see cref="Selection" />. Narrow it with <c>Returning</c> and with the member adjectives
///     (<c>WithSuffix</c>, <c>WithPrefix</c>, <c>WithNameMatching</c>, <c>AttributedWith</c>,
///     <c>ThatAreStatic</c>, <c>Where</c>), each of which hands back a method selection again, so the
///     method-only calls stay reachable whatever the order
///     (<c>.Methods.WithSuffix("Async").Returning(typeof(Task))</c> compiles, and so does the reverse).
///     Finish it with a member verb such as <c>MustHaveSuffix</c>, <c>MustBePublic</c> or
///     <c>MustBeStatic</c>, or with <c>MustAcceptParameter</c>, which methods alone accept. Immutable
///     and reusable: every call hands back a new selection and leaves this one as it was.
/// </summary>
public sealed class MethodSelection : MemberSelection
{
    internal MethodSelection(Selection source, IReadOnlyList<MemberAdjective> adjectives)
        : base(source, MemberKindFilter.Method, adjectives)
    {
    }

    /// <summary>
    ///     Narrows the selection to the methods whose return type is one of the given types, such as
    ///     <c>.Methods.Returning(typeof(Task), typeof(Task&lt;&gt;))</c>. A non-generic type matches
    ///     exactly; an open generic definition (<c>typeof(Task&lt;&gt;)</c>) matches every construction of
    ///     it, <c>Task&lt;int&gt;</c> and <c>Task&lt;Order&gt;</c> alike. Matching is by the type named and
    ///     nothing wider: a return type merely derived from it, or assignable to it, does not match. A
    ///     constructed generic (<c>typeof(Task&lt;int&gt;)</c>) is reported when the spec is loaded, naming
    ///     the open definition to use instead. At least one type is required, and one call takes types or
    ///     strings, never a mix: a list that needs a name for a type the spec project cannot reference is
    ///     spelled all names. Available on methods alone; keep narrowing afterwards, or finish with a
    ///     member verb.
    /// </summary>
    public MethodSelection Returning(Type first, params Type[] more)
    {
        IReadOnlyList<TypeAnchor> anchors = TypeAnchor.FromTypes(first, more);
        return (MethodSelection)Refined(new ReturningAdjective(anchors));
    }

    /// <summary>
    ///     Narrows the selection to the methods whose return type is one of the named types, the form for
    ///     a return type the spec project cannot compile against, so naming it costs no package reference.
    ///     Each name is the type definition's full name as a report prints it, namespace and containing
    ///     types included and a generic spelled with its declared type-parameter names:
    ///     <c>.Methods.Returning("System.Threading.Tasks.Task&lt;TResult&gt;")</c> matches every
    ///     construction of that type. A constructed spelling
    ///     (<c>"System.Threading.Tasks.Task&lt;System.Int32&gt;"</c>) names no definition and matches
    ///     nothing. At least one name is required, and only a blank one is reported when the spec is
    ///     loaded, so a misspelled name matches nothing without complaint, and where it is the only name,
    ///     the rule fails because its subject matched no member. One call takes strings or types, never a
    ///     mix, so a list that needs a name for one type is spelled all names, the open generic included.
    ///     Prefer <c>typeof</c> whenever the type is referenceable: the compiler checks a <c>typeof</c>,
    ///     and nothing checks a string. Available on methods alone.
    /// </summary>
    public MethodSelection Returning(string first, params string[] more)
    {
        IReadOnlyList<TypeAnchor> anchors = TypeAnchor.FromNames(first, more);
        return (MethodSelection)Refined(new ReturningAdjective(anchors));
    }

    private protected override MemberSelection Rebuild(IReadOnlyList<MemberAdjective> adjectives)
    {
        return new MethodSelection(Source, adjectives);
    }
}
