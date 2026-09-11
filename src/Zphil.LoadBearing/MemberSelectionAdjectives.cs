using Zphil.LoadBearing.Fluent;
using Zphil.LoadBearing.Model;
using static Zphil.LoadBearing.Internal.Guard;

namespace Zphil.LoadBearing;

/// <summary>
///     The v1 member-adjective vocabulary (GRAMMAR §5.7) as <b>generic self-type</b> extension methods
///     on <see cref="MemberSelection" />.
/// </summary>
/// <remarks>
///     The <c>TSelf</c> shape preserves the concrete member-selection type, so refining a
///     <see cref="MethodSelection" /> returns a <see cref="MethodSelection" /> and <c>.Returning</c>
///     stays reachable after any adjective. Each call appends one closed-vocabulary adjective and
///     returns a fresh selection (member selections are immutable values). These live on a hierarchy
///     disjoint from <see cref="Selection" />, so the identically-named type-side adjectives never
///     collide on overload resolution — the receiver type decides.
/// </remarks>
public static class MemberSelectionAdjectives
{
    /// <summary>Narrows to members whose name ends with a suffix: " named `*Async`".</summary>
    public static TSelf WithSuffix<TSelf>(this TSelf selection, string suffix)
        where TSelf : MemberSelection
    {
        return Append(selection, new MemberWithSuffixAdjective(NotNull(suffix, nameof(suffix))));
    }

    /// <summary>Narrows to members whose name starts with a prefix: " named `Get*`".</summary>
    public static TSelf WithPrefix<TSelf>(this TSelf selection, string prefix)
        where TSelf : MemberSelection
    {
        return Append(selection, new MemberWithPrefixAdjective(NotNull(prefix, nameof(prefix))));
    }

    /// <summary>Narrows to members whose name matches a glob: " whose name matches `*Handler*`".</summary>
    public static TSelf WithNameMatching<TSelf>(this TSelf selection, string glob)
        where TSelf : MemberSelection
    {
        return Append(selection, new MemberWithNameMatchingAdjective(NotNull(glob, nameof(glob))));
    }

    /// <summary>
    ///     Narrows to members carrying an attribute: "`[McpServerTool]`-attributed methods of …". Unlike the
    ///     type-side twin the fragment premodifies the member head, so a member-attributed subject can never
    ///     be misread as a type-attributed one (GRAMMAR §5.7, §6).
    /// </summary>
    public static TSelf AttributedWith<TSelf>(this TSelf selection, Type attributeType)
        where TSelf : MemberSelection
    {
        TypeAnchor anchor = TypeAnchor.FromType(NotNull(attributeType, nameof(attributeType)));
        return Append(selection, new MemberAttributedWithAdjective(anchor));
    }

    /// <summary>
    ///     Narrows to members carrying an attribute named by string — the escape hatch for an attribute the
    ///     spec project cannot compile against, so it need not take a package reference just to write the
    ///     <c>typeof</c>. <paramref name="attributeFullName" /> is the attribute <em>definition</em>'s
    ///     fully-qualified name in extraction format, <c>Attribute</c> suffix included
    ///     (<c>"ModelContextProtocol.Server.McpServerToolAttribute"</c>); it matches any construction of
    ///     that definition, and a constructed spelling matches nothing. Prefer
    ///     <see cref="AttributedWith{TSelf}(TSelf,Type)" /> whenever the attribute is referenceable — the
    ///     compiler checks a <c>typeof</c>, and nothing checks a string.
    /// </summary>
    public static TSelf AttributedWith<TSelf>(this TSelf selection, string attributeFullName)
        where TSelf : MemberSelection
    {
        TypeAnchor anchor = TypeAnchor.FromName(NotNull(attributeFullName, nameof(attributeFullName)));
        return Append(selection, new MemberAttributedWithAdjective(anchor));
    }

    /// <summary>
    ///     Narrows to members declared <c>static</c>: "static fields of …". Like the attribute twin the
    ///     fragment premodifies the member head, so the fact stays attached to the noun it narrows, and the
    ///     two prefixes concatenate in authoring order when both are present (GRAMMAR §5.7, §6).
    /// </summary>
    public static TSelf ThatAreStatic<TSelf>(this TSelf selection)
        where TSelf : MemberSelection
    {
        return Append(selection, new MemberThatAreStaticAdjective());
    }

    // The generic twin is receiver-typed rather than TSelf-generic, and that asymmetry with every other
    // adjective in this file is deliberate: C# has no PARTIAL type inference, so a two-type-parameter
    // AttributedWith<TSelf, T>(this TSelf, ...) would force `selection.AttributedWith<MethodSelection,
    // MyAttribute>()` at every call site — the sugar's whole point is that the type argument is the only
    // thing written. One overload per receiver recovers what TSelf was there for, so the set has to cover
    // every concrete member selection there is: MethodSelection keeps `.Returning` and MustAcceptParameter
    // reachable after the sugar, PropertySelection keeps MustBeGetOnly, FieldSelection keeps MustBeReadonly,
    // and the MemberSelection overload serves .Members/.Events, whose KindMemberSelection is internal. A new
    // projection type ships its overload here or its kind-only verb silently stops compiling after the
    // sugar. Do not "fix" this back to TSelf.

    /// <summary>
    ///     Narrows to members carrying attribute <typeparamref name="T" /> —
    ///     <c>≡ AttributedWith(typeof(T))</c>; an open generic stays <c>typeof</c>.
    /// </summary>
    public static MemberSelection AttributedWith<T>(this MemberSelection selection)
        where T : Attribute
    {
        return selection.AttributedWith(typeof(T));
    }

    /// <summary>
    ///     Narrows to methods carrying attribute <typeparamref name="T" /> —
    ///     <c>≡ AttributedWith(typeof(T))</c>. The <see cref="MethodSelection" /> receiver is what keeps
    ///     <see cref="MethodSelection.Returning(Type,Type[])" /> and <c>MustAcceptParameter</c> reachable
    ///     after the sugar.
    /// </summary>
    public static MethodSelection AttributedWith<T>(this MethodSelection selection)
        where T : Attribute
    {
        return selection.AttributedWith(typeof(T));
    }

    /// <summary>
    ///     Narrows to properties carrying attribute <typeparamref name="T" /> —
    ///     <c>≡ AttributedWith(typeof(T))</c>. The <see cref="PropertySelection" /> receiver is what keeps
    ///     <c>MustBeGetOnly</c> reachable after the sugar.
    /// </summary>
    public static PropertySelection AttributedWith<T>(this PropertySelection selection)
        where T : Attribute
    {
        return selection.AttributedWith(typeof(T));
    }

    /// <summary>
    ///     Narrows to fields carrying attribute <typeparamref name="T" /> —
    ///     <c>≡ AttributedWith(typeof(T))</c>. The <see cref="FieldSelection" /> receiver is what keeps
    ///     <c>MustBeReadonly</c> reachable after the sugar.
    /// </summary>
    public static FieldSelection AttributedWith<T>(this FieldSelection selection)
        where T : Attribute
    {
        return selection.AttributedWith(typeof(T));
    }

    /// <summary>
    ///     The member selector-position escape hatch. The predicate is stored, never evaluated at
    ///     spec build; the required <paramref name="description" /> renders as a sentence-final relative
    ///     clause (GRAMMAR §5.6, §5.7). A blank description fails spec build (validation §8 item 5).
    /// </summary>
    public static TSelf Where<TSelf>(this TSelf selection, Func<IMemberInfo, bool> predicate, string description)
        where TSelf : MemberSelection
    {
        return Append(selection, new MemberWhereAdjective(NotNull(predicate, nameof(predicate)), description));
    }

    private static TSelf Append<TSelf>(TSelf selection, MemberAdjective adjective)
        where TSelf : MemberSelection
    {
        NotNull(selection, nameof(selection));
        return (TSelf)selection.Refined(adjective);
    }
}
