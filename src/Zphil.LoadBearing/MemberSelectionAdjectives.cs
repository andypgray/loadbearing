using Zphil.LoadBearing.Fluent;
using Zphil.LoadBearing.Model;
using static Zphil.LoadBearing.Internal.Guard;

namespace Zphil.LoadBearing;

/// <summary>
///     The adjectives that narrow a member selection — the <c>Members</c>, <c>Methods</c>,
///     <c>Properties</c>, <c>Fields</c> or <c>Events</c> projection of a <see cref="Selection" />.
///     Each returns a new selection carrying one more condition and leaves the original untouched,
///     keeping the projection's own type: <c>Returning</c> and <c>MustAcceptParameter</c> stay
///     reachable on a <see cref="MethodSelection" /> after any adjective, <c>MustBeGetOnly</c> on a
///     <see cref="PropertySelection" /> and <c>MustBeReadonly</c> on a <see cref="FieldSelection" />.
///     Finish with a member verb such as <c>MustBePublic</c> or <c>MustHaveSuffix</c>.
/// </summary>
// The TSelf shape is what preserves the concrete member-selection type through a refinement. These
// live on a hierarchy disjoint from Selection, so the identically-named type-side adjectives never
// collide on overload resolution — the receiver type decides (GRAMMAR §5.7).
public static class MemberSelectionAdjectives
{
    /// <summary>
    ///     Narrows the selection to the members whose name ends with a suffix, such as
    ///     <c>arch.Types.Methods.WithSuffix("Async")</c>. The comparison is case-sensitive and literal
    ///     over the member's own name. Literal means the suffix is not a glob: a <c>*</c> in it matches
    ///     a <c>*</c>, so over a type declaring <c>LoadAsync</c>, <c>WithSuffix("*Async")</c> selects
    ///     nothing while <c>WithNameMatching("*Async")</c> selects it. A blank suffix is reported when
    ///     the spec is loaded. <see cref="WithNameMatching{TSelf}" /> is the glob form beside this one.
    /// </summary>
    public static TSelf WithSuffix<TSelf>(this TSelf selection, string suffix)
        where TSelf : MemberSelection
    {
        return Append(selection, new MemberWithSuffixAdjective(NotNull(suffix, nameof(suffix))));
    }

    /// <summary>
    ///     Narrows the selection to the members whose name starts with a prefix, such as
    ///     <c>arch.Types.Methods.WithPrefix("Get")</c>. The comparison is case-sensitive and literal
    ///     over the member's own name. Literal means the prefix is not a glob: a <c>*</c> in it matches
    ///     a <c>*</c>, so over a type declaring <c>GetOrder</c>, <c>WithPrefix("Get*")</c> selects
    ///     nothing while <c>WithNameMatching("Get*")</c> selects it. A blank prefix is reported when the
    ///     spec is loaded. <see cref="WithNameMatching{TSelf}" /> is the glob form beside this one.
    /// </summary>
    public static TSelf WithPrefix<TSelf>(this TSelf selection, string prefix)
        where TSelf : MemberSelection
    {
        return Append(selection, new MemberWithPrefixAdjective(NotNull(prefix, nameof(prefix))));
    }

    /// <summary>
    ///     Narrows the selection to the members whose name matches a glob, such as
    ///     <c>arch.Types.Members.WithNameMatching("*Handler*")</c>. Matching is case-sensitive: <c>*</c>
    ///     matches any run of characters including none, every other character matches itself, a pattern
    ///     with no <c>*</c> is an exact name match, and a lone <c>*</c> matches every name. A blank glob is
    ///     reported when the spec is loaded.
    /// </summary>
    public static TSelf WithNameMatching<TSelf>(this TSelf selection, string glob)
        where TSelf : MemberSelection
    {
        return Append(selection, new MemberWithNameMatchingAdjective(NotNull(glob, nameof(glob))));
    }

    /// <summary>
    ///     Narrows the selection to the members carrying an attribute, such as
    ///     <c>arch.Types.Methods.AttributedWith(typeof(HttpGetAttribute))</c>. These are the members that
    ///     carry the attribute, not the members of types that carry it — for that, apply
    ///     <c>AttributedWith</c> to the type selection before projecting to members. Attributes written on
    ///     the member itself count and no others: an attribute on a property's <c>get</c> or <c>set</c>
    ///     accessor, and a <c>[return:]</c> attribute, are not seen. Pass an open generic definition to
    ///     match every construction of it, or a constructed attribute type to match that one exactly.
    ///     Nothing checks that the type is an attribute: given anything else it simply matches nothing,
    ///     and a rule whose subject matches no member fails the check.
    /// </summary>
    public static TSelf AttributedWith<TSelf>(this TSelf selection, Type attributeType)
        where TSelf : MemberSelection
    {
        TypeAnchor anchor = TypeAnchor.FromType(NotNull(attributeType, nameof(attributeType)));
        return Append(selection, new MemberAttributedWithAdjective(anchor));
    }

    /// <summary>
    ///     Narrows the selection to the members carrying the attribute with this name — the form to use
    ///     for an attribute the spec project cannot compile against, so it need not take a package
    ///     reference just to write the <c>typeof</c>. <paramref name="attributeFullName" /> is the
    ///     attribute definition's fully-qualified name with the <c>Attribute</c> suffix included
    ///     (<c>"ModelContextProtocol.Server.McpServerToolAttribute"</c>); it matches every construction of
    ///     that definition, and a constructed spelling matches nothing. Attributes written on the member
    ///     itself count and no others. Blankness is the only thing checked, and a blank name is reported
    ///     when the spec is loaded: a misspelt name is a legal name that matches nothing. Prefer the
    ///     <see cref="System.Type" /> overload whenever the attribute is referenceable, because the
    ///     compiler checks a <c>typeof</c> and nothing checks a string.
    /// </summary>
    public static TSelf AttributedWith<TSelf>(this TSelf selection, string attributeFullName)
        where TSelf : MemberSelection
    {
        TypeAnchor anchor = TypeAnchor.FromName(NotNull(attributeFullName, nameof(attributeFullName)));
        return Append(selection, new MemberAttributedWithAdjective(anchor));
    }

    /// <summary>
    ///     Narrows the selection to the members declared <c>static</c>, such as
    ///     <c>arch.Types.Fields.ThatAreStatic()</c>. Instance members are dropped.
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
    ///     Narrows the selection to the members carrying attribute <typeparamref name="T" />, such as
    ///     <c>arch.Types.Members.AttributedWith&lt;ObsoleteAttribute&gt;()</c>. These are the members that
    ///     carry the attribute, not the members of types that carry it. Attributes written on the member
    ///     itself count and no others: an attribute on a property's <c>get</c> or <c>set</c> accessor, and
    ///     a <c>[return:]</c> attribute, are not seen. An open generic attribute has no type-argument
    ///     form: for <c>MarkAttribute&lt;&gt;</c> use the <see cref="System.Type" /> overload and pass
    ///     <c>typeof(MarkAttribute&lt;&gt;)</c>, which matches every construction of it.
    /// </summary>
    public static MemberSelection AttributedWith<T>(this MemberSelection selection)
        where T : Attribute
    {
        return selection.AttributedWith(typeof(T));
    }

    /// <summary>
    ///     Narrows the selection to the methods carrying attribute <typeparamref name="T" />, such as
    ///     <c>arch.Types.Methods.AttributedWith&lt;ObsoleteAttribute&gt;()</c>. Attributes written on the
    ///     method itself count and no others. The result is still a <see cref="MethodSelection" />, so
    ///     <c>Returning</c> and <c>MustAcceptParameter</c> stay reachable after it. An open generic
    ///     attribute has no type-argument form: for <c>MarkAttribute&lt;&gt;</c> use the
    ///     <see cref="System.Type" /> overload and pass <c>typeof(MarkAttribute&lt;&gt;)</c>, which
    ///     matches every construction of it.
    /// </summary>
    public static MethodSelection AttributedWith<T>(this MethodSelection selection)
        where T : Attribute
    {
        return selection.AttributedWith(typeof(T));
    }

    /// <summary>
    ///     Narrows the selection to the properties carrying attribute <typeparamref name="T" />, such as
    ///     <c>arch.Types.Properties.AttributedWith&lt;ObsoleteAttribute&gt;()</c>. Attributes written on
    ///     the property itself count and no others, so one on its <c>get</c> or <c>set</c> accessor is not
    ///     seen. The result is still a <see cref="PropertySelection" />, so <c>MustBeGetOnly</c> stays
    ///     reachable after it. An open generic attribute has no type-argument form: for
    ///     <c>MarkAttribute&lt;&gt;</c> use the <see cref="System.Type" /> overload and pass
    ///     <c>typeof(MarkAttribute&lt;&gt;)</c>, which matches every construction of it.
    /// </summary>
    public static PropertySelection AttributedWith<T>(this PropertySelection selection)
        where T : Attribute
    {
        return selection.AttributedWith(typeof(T));
    }

    /// <summary>
    ///     Narrows the selection to the fields carrying attribute <typeparamref name="T" />, such as
    ///     <c>arch.Types.Fields.AttributedWith&lt;ObsoleteAttribute&gt;()</c>. Attributes written on the
    ///     field itself count and no others. The result is still a <see cref="FieldSelection" />, so
    ///     <c>MustBeReadonly</c> stays reachable after it. An open generic attribute has no type-argument
    ///     form: for <c>MarkAttribute&lt;&gt;</c> use the <see cref="System.Type" /> overload and pass
    ///     <c>typeof(MarkAttribute&lt;&gt;)</c>, which matches every construction of it.
    /// </summary>
    public static FieldSelection AttributedWith<T>(this FieldSelection selection)
        where T : Attribute
    {
        return selection.AttributedWith(typeof(T));
    }

    /// <summary>
    ///     Narrows the selection with a predicate of your own, for what the member adjectives cannot say:
    ///     <c>
    ///         arch.Types.Methods.Where(m =&gt; m.Parameters.Count &gt; 5, description: "with more than
    ///         five parameters")
    ///     </c>
    ///     . The predicate reads the facts on <see cref="IMemberInfo" />, its
    ///     declaring type's included, and runs against each candidate member when the check runs, never
    ///     when the spec is loaded. <paramref name="description" /> is required and completes the subject
    ///     as a relative clause; it is rendered verbatim in the generated agent context in place of the
    ///     wording an adjective would produce, last in the clause list, and nothing checks that it
    ///     describes what the predicate does. A blank or multi-line description is reported when the spec
    ///     is loaded.
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
