using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing.Fluent;

/// <summary>
///     The member selection minted by the <c>.Methods</c> projection (GRAMMAR §4.6) — a
///     <see cref="MemberSelection" /> specialized to methods that additionally offers
///     <see cref="Returning(Type,Type[])" />.
/// </summary>
/// <remarks>
///     The return-type adjective is methods-only, so it lives here and is uncompilable on the other
///     projections by construction (GRAMMAR §3.2). The shared member adjectives preserve this type
///     (they are generic self-type extensions), so
///     <c>.Methods.WithSuffix("Async").Returning(typeof(Task))</c> type-checks in any order.
/// </remarks>
public sealed class MethodSelection : MemberSelection
{
    internal MethodSelection(Selection source, IReadOnlyList<MemberAdjective> adjectives)
        : base(source, MemberKindFilter.Method, adjectives)
    {
    }

    /// <summary>
    ///     Narrows to methods whose return type matches one of the anchors, definition-level (GRAMMAR
    ///     §4.6): a non-generic anchor (<c>typeof(Task)</c>) matches exactly, an open-generic anchor
    ///     (<c>typeof(Task&lt;&gt;)</c>) matches any construction. A closed-generic anchor is refused at
    ///     spec build (GRAMMAR §8 item 14). The <c>(first, more)</c> shape makes a zero-anchor call
    ///     uncompilable.
    /// </summary>
    public MethodSelection Returning(Type first, params Type[] more)
    {
        IReadOnlyList<TypeAnchor> anchors = TypeAnchor.FromTypes(first, more);
        return (MethodSelection)Refined(new ReturningAdjective(anchors));
    }

    /// <summary>
    ///     Narrows to methods whose return type matches one of the anchors named by string — the escape
    ///     hatch for a return type the spec project cannot compile against, so it need not take a package
    ///     reference just to write the <c>typeof</c>. Each name is the return type <em>definition</em>'s
    ///     fully-qualified name as a report prints it, declared type-parameter names for a generic
    ///     (<c>"System.Threading.Tasks.Task&lt;TResult&gt;"</c>); it matches any construction of that
    ///     definition, and a constructed spelling matches nothing. The overloads are homogeneous — one
    ///     call is all <c>typeof</c> or all names — so a list that needs a name for one type is spelled
    ///     all names, the open generic included. Prefer <see cref="Returning(Type,Type[])" /> whenever the
    ///     type is referenceable — the compiler checks a <c>typeof</c>, and nothing checks a string.
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
