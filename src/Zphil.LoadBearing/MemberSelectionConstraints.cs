using Zphil.LoadBearing.Fluent;
using Zphil.LoadBearing.Model;
using static Zphil.LoadBearing.Internal.Guard;

namespace Zphil.LoadBearing;

/// <summary>
///     The v1 member modal-constraint vocabulary (GRAMMAR §5.7) as extension methods that turn a
///     <see cref="MemberSelection" /> into a terminal <see cref="Constraint" />.
/// </summary>
/// <remarks>
///     Polarity is lexical, exactly like the type-side verbs (GRAMMAR §2), and the naming verbs reuse
///     the type-side "must be named" / "must have a name matching" fragments. These bind by receiver
///     type (a <see cref="MemberSelection" /> is not a <see cref="Selection" />), so the
///     identically-named type-side verbs never collide on overload resolution.
/// </remarks>
public static class MemberSelectionConstraints
{
    /// <summary>The subject members' names must end with a suffix.</summary>
    public static Constraint MustHaveSuffix(this MemberSelection subject, string suffix)
    {
        return new MemberMustHaveSuffixConstraint(Subject(subject), NotNull(suffix, nameof(suffix)));
    }

    /// <summary>The subject members' names must start with a prefix.</summary>
    public static Constraint MustHavePrefix(this MemberSelection subject, string prefix)
    {
        return new MemberMustHavePrefixConstraint(Subject(subject), NotNull(prefix, nameof(prefix)));
    }

    /// <summary>The subject members' names must match a glob.</summary>
    public static Constraint MustHaveNameMatching(this MemberSelection subject, string glob)
    {
        return new MemberMustHaveNameMatchingConstraint(Subject(subject), NotNull(glob, nameof(glob)));
    }

    /// <summary>The subject members must be public.</summary>
    public static Constraint MustBePublic(this MemberSelection subject)
    {
        return new MemberMustBePublicConstraint(Subject(subject));
    }

    /// <summary>The subject members must be internal.</summary>
    public static Constraint MustBeInternal(this MemberSelection subject)
    {
        return new MemberMustBeInternalConstraint(Subject(subject));
    }

    /// <summary>The subject members must be private (member-only vocabulary).</summary>
    public static Constraint MustBePrivate(this MemberSelection subject)
    {
        return new MemberMustBePrivateConstraint(Subject(subject));
    }

    /// <summary>The subject members must be static.</summary>
    public static Constraint MustBeStatic(this MemberSelection subject)
    {
        return new MemberMustBeStaticConstraint(Subject(subject));
    }

    /// <summary>The subject members must be abstract.</summary>
    public static Constraint MustBeAbstract(this MemberSelection subject)
    {
        return new MemberMustBeAbstractConstraint(Subject(subject));
    }

    /// <summary>The subject members must be virtual (member-only vocabulary).</summary>
    public static Constraint MustBeVirtual(this MemberSelection subject)
    {
        return new MemberMustBeVirtualConstraint(Subject(subject));
    }

    /// <summary>The subject members must carry an attribute.</summary>
    public static Constraint MustBeAttributedWith(this MemberSelection subject, Type attributeType)
    {
        TypeAnchor anchor = TypeAnchor.FromType(NotNull(attributeType, nameof(attributeType)));
        return new MemberMustBeAttributedWithConstraint(Subject(subject), anchor);
    }

    /// <summary>
    ///     The subject members must carry an attribute named by string — the escape hatch for an attribute
    ///     the spec project cannot compile against, so it need not take a package reference just to write
    ///     the <c>typeof</c>. <paramref name="attributeFullName" /> is the attribute <em>definition</em>'s
    ///     fully-qualified name in extraction format, <c>Attribute</c> suffix included
    ///     (<c>"ModelContextProtocol.Server.McpServerToolAttribute"</c>); it matches any construction of
    ///     that definition, and a constructed spelling matches nothing. Prefer
    ///     <see cref="MustBeAttributedWith(MemberSelection,Type)" /> whenever the attribute is
    ///     referenceable — the compiler checks a <c>typeof</c>, and nothing checks a string.
    /// </summary>
    public static Constraint MustBeAttributedWith(this MemberSelection subject, string attributeFullName)
    {
        TypeAnchor anchor = TypeAnchor.FromName(NotNull(attributeFullName, nameof(attributeFullName)));
        return new MemberMustBeAttributedWithConstraint(Subject(subject), anchor);
    }

    /// <summary>
    ///     The subject members must carry attribute <typeparamref name="T" /> —
    ///     <c>≡ MustBeAttributedWith(typeof(T))</c>; an open generic stays <c>typeof</c>.
    /// </summary>
    public static Constraint MustBeAttributedWith<T>(this MemberSelection subject)
        where T : Attribute
    {
        return subject.MustBeAttributedWith(typeof(T));
    }

    /// <summary>
    ///     The subject members must not carry any of the attribute anchors — none-of semantics
    ///     (GRAMMAR §5.7, §10). The negative takes <c>(Type first, params Type[] more)</c>: "must not be
    ///     attributed with `A` or `B`" is unambiguous, unlike the single-<c>Type</c> positive.
    /// </summary>
    public static Constraint MustNotBeAttributedWith(this MemberSelection subject, Type first, params Type[] more)
    {
        return new MemberMustNotBeAttributedWithConstraint(Subject(subject), TypeAnchor.FromTypes(first, more));
    }

    /// <summary>
    ///     The subject members must not carry any of the attribute anchors named by string — none-of
    ///     semantics (GRAMMAR §5.7, §10) over the escape-hatch form, for attributes the spec project cannot
    ///     compile against. Each name is an attribute <em>definition</em>'s fully-qualified name in
    ///     extraction format, <c>Attribute</c> suffix included
    ///     (<c>"ModelContextProtocol.Server.McpServerToolAttribute"</c>), matching any construction of that
    ///     definition; a constructed spelling matches nothing. The overloads are homogeneous — one call is
    ///     all <c>typeof</c> or all names; write a second rule to mix them. Prefer
    ///     <see cref="MustNotBeAttributedWith(MemberSelection,Type,Type[])" /> whenever the attributes are
    ///     referenceable — the compiler checks a <c>typeof</c>, and nothing checks a string.
    /// </summary>
    public static Constraint MustNotBeAttributedWith(this MemberSelection subject, string first, params string[] more)
    {
        return new MemberMustNotBeAttributedWithConstraint(Subject(subject), TypeAnchor.FromNames(first, more));
    }

    /// <summary>
    ///     The subject members must not carry attribute <typeparamref name="T" /> —
    ///     <c>≡ MustNotBeAttributedWith(typeof(T))</c>; an open generic stays <c>typeof</c>.
    /// </summary>
    public static Constraint MustNotBeAttributedWith<T>(this MemberSelection subject)
        where T : Attribute
    {
        return subject.MustNotBeAttributedWith(typeof(T));
    }

    /// <summary>
    ///     The member constraint-position escape hatch. The predicate is stored, never evaluated at
    ///     spec build; the required <paramref name="description" /> completes "must …". A blank
    ///     description fails spec build (validation §8 item 5).
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
