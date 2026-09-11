using Zphil.LoadBearing.Fluent;
using Zphil.LoadBearing.Model;
using static Zphil.LoadBearing.Internal.Guard;

namespace Zphil.LoadBearing;

/// <summary>
///     The verbs that finish a member selection — the <c>Members</c>, <c>Methods</c>,
///     <c>Properties</c>, <c>Fields</c> or <c>Events</c> projection of a <see cref="Selection" />.
///     Each states a rule about the selected members and returns the <see cref="Constraint" /> to
///     hand to <c>Enforce</c> or <c>Migrate</c>; the check then reports one violation per member that
///     breaks the rule, naming the member and the line it is declared on. A rule whose member
///     selection matches no member fails as well.
/// </summary>
// Negation lives in the verb name, never in a Not() combinator (GRAMMAR §2), and the naming verbs
// reuse the type-side "must be named" / "must have a name matching" fragments. These bind by receiver
// type — a MemberSelection is not a Selection — so the identically-named type-side verbs never collide
// on overload resolution.
public static class MemberSelectionConstraints
{
    /// <summary>
    ///     States that every selected member's name must end with a suffix, such as
    ///     <c>arch.Types.Methods.Returning(typeof(Task)).MustHaveSuffix("Async")</c>. The comparison is
    ///     case-sensitive and literal over the member's own name, and each member whose name does not
    ///     end with it fails the check. The suffix is literal text rather than a glob, so a <c>*</c> in
    ///     it matches a <c>*</c>; for a pattern with wildcards use <see cref="MustHaveNameMatching" />.
    ///     A blank suffix is reported when the spec is loaded.
    /// </summary>
    public static Constraint MustHaveSuffix(this MemberSelection subject, string suffix)
    {
        return new MemberMustHaveSuffixConstraint(Subject(subject), NotNull(suffix, nameof(suffix)));
    }

    /// <summary>
    ///     States that every selected member's name must start with a prefix, such as
    ///     <c>arch.Types.Fields.ThatAreStatic().MustHavePrefix("Default")</c>. The comparison is
    ///     case-sensitive and literal over the member's own name, and each member whose name does not
    ///     start with it fails the check. The prefix is literal text rather than a glob, so a <c>*</c>
    ///     in it matches a <c>*</c>; for a pattern with wildcards use
    ///     <see cref="MustHaveNameMatching" />. A blank prefix is reported when the spec is loaded.
    /// </summary>
    public static Constraint MustHavePrefix(this MemberSelection subject, string prefix)
    {
        return new MemberMustHavePrefixConstraint(Subject(subject), NotNull(prefix, nameof(prefix)));
    }

    /// <summary>
    ///     States that every selected member's name must match a glob, such as
    ///     <c>arch.Types.Properties.MustHaveNameMatching("*Id")</c>. Matching is case-sensitive: <c>*</c>
    ///     matches any run of characters including none, every other character matches itself, a pattern
    ///     with no <c>*</c> is an exact name match, and a lone <c>*</c> matches every name. Each member
    ///     whose name does not match fails the check. A blank glob is reported when the spec is loaded.
    /// </summary>
    public static Constraint MustHaveNameMatching(this MemberSelection subject, string glob)
    {
        return new MemberMustHaveNameMatchingConstraint(Subject(subject), NotNull(glob, nameof(glob)));
    }

    /// <summary>
    ///     States that every selected member must be declared <c>public</c>. Accessibility is compared exactly,
    ///     so an <c>internal</c>, <c>protected</c>, <c>protected internal</c>, <c>private protected</c> or
    ///     <c>private</c> member fails the check.
    /// </summary>
    public static Constraint MustBePublic(this MemberSelection subject)
    {
        return new MemberMustBePublicConstraint(Subject(subject));
    }

    /// <summary>
    ///     States that every selected member must be declared <c>internal</c>. Accessibility is compared
    ///     exactly, so a <c>public</c>, <c>protected</c>, <c>protected internal</c>,
    ///     <c>private protected</c> or <c>private</c> member fails the check.
    /// </summary>
    public static Constraint MustBeInternal(this MemberSelection subject)
    {
        return new MemberMustBeInternalConstraint(Subject(subject));
    }

    /// <summary>
    ///     States that every selected member must be declared <c>private</c>. Accessibility is compared
    ///     exactly, so a <c>private protected</c> member fails the check, as does any wider one. There is
    ///     no counterpart to this verb on a type selection.
    /// </summary>
    public static Constraint MustBePrivate(this MemberSelection subject)
    {
        return new MemberMustBePrivateConstraint(Subject(subject));
    }

    /// <summary>
    ///     States that every selected member must be declared <c>static</c>; an instance member fails the
    ///     check.
    /// </summary>
    public static Constraint MustBeStatic(this MemberSelection subject)
    {
        return new MemberMustBeStaticConstraint(Subject(subject));
    }

    /// <summary>
    ///     States that every selected member must be declared <c>abstract</c>. This reads the C# declaration:
    ///     every member of an interface counts as abstract, while a <c>virtual</c> member and a plain
    ///     <c>override</c> do not and fail the check.
    /// </summary>
    public static Constraint MustBeAbstract(this MemberSelection subject)
    {
        return new MemberMustBeAbstractConstraint(Subject(subject));
    }

    /// <summary>
    ///     States that every selected member must be declared <c>virtual</c>. This reads the C# declaration, so
    ///     an <c>override</c> is not virtual and neither is an <c>abstract</c> member; both fail the
    ///     check. There is no counterpart to this verb on a type selection.
    /// </summary>
    public static Constraint MustBeVirtual(this MemberSelection subject)
    {
        return new MemberMustBeVirtualConstraint(Subject(subject));
    }

    /// <summary>
    ///     States that every selected member must carry an attribute, such as
    ///     <c>arch.Types.Methods.WithPrefix("Handle").MustBeAttributedWith(typeof(HttpPostAttribute))</c>.
    ///     A member that does not carry it fails the check. Attributes written on the member itself count
    ///     and no others: an attribute on a property's <c>get</c> or <c>set</c> accessor, and a
    ///     <c>[return:]</c> attribute, are not seen. Pass an open generic definition to accept every
    ///     construction of it, or a constructed attribute type to require that one exactly. The type must
    ///     derive from <c>System.Attribute</c>; anything else, <c>typeof(Attribute)</c> itself included,
    ///     is reported when the spec is loaded.
    /// </summary>
    public static Constraint MustBeAttributedWith(this MemberSelection subject, Type attributeType)
    {
        TypeAnchor anchor = TypeAnchor.FromType(NotNull(attributeType, nameof(attributeType)));
        return new MemberMustBeAttributedWithConstraint(Subject(subject), anchor);
    }

    /// <summary>
    ///     States that every selected member must carry the attribute with this name — the form to use for an
    ///     attribute the spec project cannot compile against, so it need not take a package reference just
    ///     to write the <c>typeof</c>. <paramref name="attributeFullName" /> is the attribute definition's
    ///     fully-qualified name with the <c>Attribute</c> suffix included
    ///     (<c>"ModelContextProtocol.Server.McpServerToolAttribute"</c>); it accepts every construction of
    ///     that definition, and a constructed spelling matches nothing. Attributes written on the member
    ///     itself count and no others. Blankness is the only thing checked, and a blank name is reported
    ///     when the spec is loaded: a misspelt name is a legal name that nothing carries, so every
    ///     selected member then fails the check. Prefer the <see cref="System.Type" /> overload whenever
    ///     the attribute is referenceable, because the compiler checks a <c>typeof</c> and nothing checks
    ///     a string.
    /// </summary>
    public static Constraint MustBeAttributedWith(this MemberSelection subject, string attributeFullName)
    {
        TypeAnchor anchor = TypeAnchor.FromName(NotNull(attributeFullName, nameof(attributeFullName)));
        return new MemberMustBeAttributedWithConstraint(Subject(subject), anchor);
    }

    /// <summary>
    ///     States that every selected member must carry attribute <typeparamref name="T" />, such as
    ///     <c>arch.Types.Methods.MustBeAttributedWith&lt;HttpPostAttribute&gt;()</c>. A member that does
    ///     not carry it fails the check. Attributes written on the member itself count and no others. An
    ///     open generic attribute has no type-argument form: for <c>MarkAttribute&lt;&gt;</c> use the
    ///     <see cref="System.Type" /> overload and pass <c>typeof(MarkAttribute&lt;&gt;)</c>, which
    ///     accepts every construction of it.
    /// </summary>
    public static Constraint MustBeAttributedWith<T>(this MemberSelection subject)
        where T : Attribute
    {
        return subject.MustBeAttributedWith(typeof(T));
    }

    /// <summary>
    ///     States that no selected member may carry any of the listed attributes, such as
    ///     <c>arch.Types.Members.MustNotBeAttributedWith(typeof(ObsoleteAttribute))</c>. A member carrying
    ///     any one of them fails the check. Attributes written on the member itself count and no others.
    ///     Pass an open generic definition to ban every construction of it, or a constructed attribute
    ///     type to ban that one exactly. At least one type is required, one call takes types or names but
    ///     never a mix of the two, and each type must derive from <c>System.Attribute</c>; anything else,
    ///     <c>typeof(Attribute)</c> itself included, is reported when the spec is loaded.
    /// </summary>
    public static Constraint MustNotBeAttributedWith(this MemberSelection subject, Type first, params Type[] more)
    {
        return new MemberMustNotBeAttributedWithConstraint(Subject(subject), TypeAnchor.FromTypes(first, more));
    }

    /// <summary>
    ///     States that no selected member may carry any of the attributes with these names — the form to use
    ///     for attributes the spec project cannot compile against, so the spec need not take a package
    ///     reference just to write the <c>typeof</c>. Each name is an attribute definition's
    ///     fully-qualified name with the <c>Attribute</c> suffix included
    ///     (<c>"ModelContextProtocol.Server.McpServerToolAttribute"</c>); it bans every construction of
    ///     that definition, and a constructed spelling bans nothing. A member carrying any one of them
    ///     fails the check. At least one name is required, and one call takes names or types but never a
    ///     mix of the two. Blankness is the only thing checked, and a blank name is reported when the spec
    ///     is loaded: a misspelt name is a legal name that nothing carries, so the ban quietly passes
    ///     everything. Prefer the <see cref="System.Type" /> overload whenever the attributes are
    ///     referenceable, because the compiler checks a <c>typeof</c> and nothing checks a string.
    /// </summary>
    public static Constraint MustNotBeAttributedWith(this MemberSelection subject, string first, params string[] more)
    {
        return new MemberMustNotBeAttributedWithConstraint(Subject(subject), TypeAnchor.FromNames(first, more));
    }

    /// <summary>
    ///     States that no selected member may carry attribute <typeparamref name="T" />, such as
    ///     <c>arch.Types.Members.MustNotBeAttributedWith&lt;ObsoleteAttribute&gt;()</c>. A member carrying
    ///     it fails the check. Attributes written on the member itself count and no others. An open
    ///     generic attribute has no type-argument form: for <c>MarkAttribute&lt;&gt;</c> use the
    ///     <see cref="System.Type" /> overload and pass <c>typeof(MarkAttribute&lt;&gt;)</c>, which bans
    ///     every construction of it.
    /// </summary>
    public static Constraint MustNotBeAttributedWith<T>(this MemberSelection subject)
        where T : Attribute
    {
        return subject.MustNotBeAttributedWith(typeof(T));
    }

    /// <summary>
    ///     States that every selected member must satisfy a predicate of your own, for what the member verbs
    ///     cannot say:
    ///     <c>
    ///         arch.Types.Methods.Must(m =&gt; m.Parameters.Count &lt;= 5, description: "take
    ///         five parameters or fewer")
    ///     </c>
    ///     . The predicate reads the facts on <see cref="IMemberInfo" /> and
    ///     runs against each selected member when the check runs, never when the spec is loaded; each
    ///     member it returns <see langword="false" /> for fails the check.
    ///     <paramref name="description" /> is required and completes the phrase "must ..." as a
    ///     bare-infinitive verb phrase; it is rendered verbatim in the generated agent context and in the
    ///     check report, and nothing checks that it describes what the predicate does. A blank or
    ///     multi-line description is reported when the spec is loaded.
    /// </summary>
    public static Constraint Must(this MemberSelection subject, Func<IMemberInfo, bool> predicate, string description)
    {
        return new MemberMustConstraint(Subject(subject), NotNull(predicate, nameof(predicate)), description);
    }

    private static MemberSelection Subject(MemberSelection subject)
    {
        return NotNull(subject, nameof(subject));
    }
}
