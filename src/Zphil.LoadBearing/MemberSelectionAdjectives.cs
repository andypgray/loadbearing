using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing;

/// <summary>
///     The v1 member-adjective vocabulary (GRAMMAR §5.7) as <b>generic self-type</b> extension methods
///     on <see cref="MemberSelection" />. The <c>TSelf</c> shape preserves the concrete member-selection
///     type, so refining a <see cref="MethodSelection" /> returns a <see cref="MethodSelection" /> and
///     <c>.Returning</c> stays reachable after any adjective. Each call appends one closed-vocabulary
///     adjective and returns a fresh selection (member selections are immutable values). These live on
///     a hierarchy disjoint from <see cref="Selection" />, so the identically-named type-side adjectives
///     never collide on overload resolution — the receiver type decides.
/// </summary>
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

    // The generic twin is receiver-typed rather than TSelf-generic, and that asymmetry with every other
    // adjective in this file is deliberate: C# has no PARTIAL type inference, so a two-type-parameter
    // AttributedWith<TSelf, T>(this TSelf, ...) would force `selection.AttributedWith<MethodSelection,
    // MyAttribute>()` at every call site — the sugar's whole point is that the type argument is the only
    // thing written. One overload per receiver recovers what TSelf was there for: the MethodSelection
    // overload keeps `.Returning` and MustAcceptParameter reachable after the sugar, and KindMemberSelection
    // is internal, so no other concrete member selection exists to lose. Do not "fix" this back to TSelf.

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
    ///     <see cref="MethodSelection.Returning" /> and <c>MustAcceptParameter</c> reachable after the sugar.
    /// </summary>
    public static MethodSelection AttributedWith<T>(this MethodSelection selection)
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
        Guard.NotNull(selection, nameof(selection));
        return (TSelf)selection.Refined(adjective);
    }

    private static T NotNull<T>(T value, string paramName)
        where T : class
    {
        return Guard.NotNull(value, paramName);
    }
}