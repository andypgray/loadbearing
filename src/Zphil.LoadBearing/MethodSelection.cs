using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing;

/// <summary>
///     The member selection minted by the <c>.Methods</c> projection (GRAMMAR §4.6). A
///     <see cref="MemberSelection" /> specialized to methods that additionally offers
///     <see cref="Returning" /> — the return-type adjective is methods-only, so it lives here and is
///     uncompilable on the other projections by construction (GRAMMAR §3.2). The shared member
///     adjectives preserve this type (they are generic self-type extensions), so
///     <c>.Methods.WithSuffix("Async").Returning(typeof(Task))</c> type-checks in any order.
/// </summary>
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
        var types = new List<Type>(1 + more.Length) { Guard.NotNull(first, nameof(first)) };
        foreach (Type type in more) types.Add(Guard.NotNull(type, nameof(more)));

        return (MethodSelection)Refined(new ReturningAdjective(types));
    }

    private protected override MemberSelection Rebuild(IReadOnlyList<MemberAdjective> adjectives)
    {
        return new MethodSelection(Source, adjectives);
    }
}