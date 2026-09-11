using System.Linq.Expressions;
using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Model;
using static Zphil.LoadBearing.Internal.Guard;

namespace Zphil.LoadBearing;

/// <summary>
///     The v1 modal-constraint vocabulary (GRAMMAR §5.3) as extension methods that turn a
///     <see cref="Selection" /> into a terminal <see cref="Constraint" />.
/// </summary>
/// <remarks>
///     Polarity is lexical — negation lives in the verb name, never a <c>Not()</c> combinator
///     (GRAMMAR §2). The dependency verbs carry both overloads (GRAMMAR §3.3): a
///     <see cref="Selection" /> list and a <see cref="Type" /> list (sugar that wraps each type as a
///     single-type selection). The <c>(first, params more)</c> shape makes a zero-target call
///     uncompilable; the one shape that legitimately names no target gets a verb of its own instead
///     (<see cref="MustOnlyReferenceItself" />), so the arity stays a compile error rather than a
///     runtime one.
/// </remarks>
public static class SelectionConstraints
{
    /// <summary>The subject must not reference any of the targets.</summary>
    public static Constraint MustNotReference(this Selection subject, Selection first, params Selection[] more)
    {
        return new MustNotReferenceConstraint(subject, Selections(subject, first, more));
    }

    /// <summary>The subject must not reference any of the targets (type sugar).</summary>
    public static Constraint MustNotReference(this Selection subject, Type first, params Type[] more)
    {
        return new MustNotReferenceConstraint(subject, WrappedTypes(subject, first, more));
    }

    /// <summary>The subject may reference only the targets (external packages exempt, GRAMMAR §4.1).</summary>
    public static Constraint MustOnlyReference(this Selection subject, Selection first, params Selection[] more)
    {
        return new MustOnlyReferenceConstraint(subject, Selections(subject, first, more));
    }

    /// <summary>The subject may reference only the targets (type sugar).</summary>
    public static Constraint MustOnlyReference(this Selection subject, Type first, params Type[] more)
    {
        return new MustOnlyReferenceConstraint(subject, WrappedTypes(subject, first, more));
    }

    /// <summary>
    ///     The subject may reference nothing outside itself — the leaf of the reference graph
    ///     (external packages exempt, GRAMMAR §4.1).
    /// </summary>
    public static Constraint MustOnlyReferenceItself(this Selection subject)
    {
        return new MustOnlyReferenceItselfConstraint(Subject(subject));
    }

    /// <summary>The subject must not be referenced by any of the sources.</summary>
    public static Constraint MustNotBeReferencedBy(this Selection subject, Selection first, params Selection[] more)
    {
        return new MustNotBeReferencedByConstraint(subject, Selections(subject, first, more));
    }

    /// <summary>The subject must not be referenced by any of the sources (type sugar).</summary>
    public static Constraint MustNotBeReferencedBy(this Selection subject, Type first, params Type[] more)
    {
        return new MustNotBeReferencedByConstraint(subject, WrappedTypes(subject, first, more));
    }

    /// <summary>The subject may be referenced only by the sources.</summary>
    public static Constraint MustOnlyBeReferencedBy(this Selection subject, Selection first, params Selection[] more)
    {
        return new MustOnlyBeReferencedByConstraint(subject, Selections(subject, first, more));
    }

    /// <summary>The subject may be referenced only by the sources (type sugar).</summary>
    public static Constraint MustOnlyBeReferencedBy(this Selection subject, Type first, params Type[] more)
    {
        return new MustOnlyBeReferencedByConstraint(subject, WrappedTypes(subject, first, more));
    }

    /// <summary>The subject must not use any of the member targets (GRAMMAR §4.5).</summary>
    public static Constraint MustNotUse(this Selection subject, Member first, params Member[] more)
    {
        return new MustNotUseConstraint(subject, Members(subject, first, more));
    }

    /// <summary>
    ///     The subject must not use any of the static value-member targets — <c>() =&gt; Type.M</c>, pure
    ///     authoring sugar for <c>arch.Member(() =&gt; Type.M)</c> (GRAMMAR §3.3/§4.5). Each lambda desugars
    ///     at mint through <see cref="MemberExpressionResolver" /> to the identical <see cref="Member" /> leaf.
    /// </summary>
    public static Constraint MustNotUse(this Selection subject, Expression<Func<object?>> first, params Expression<Func<object?>>[] more)
    {
        return new MustNotUseConstraint(subject, ResolvedMembers(subject, first, more));
    }

    /// <summary>
    ///     The subject must not use any of the static void-method targets — <c>() =&gt; Type.M()</c>, pure
    ///     authoring sugar for <c>arch.Member(() =&gt; Type.M())</c> (GRAMMAR §3.3/§4.5). Each lambda desugars
    ///     at mint through <see cref="MemberExpressionResolver" /> to the identical <see cref="Member" /> leaf.
    /// </summary>
    public static Constraint MustNotUse(this Selection subject, Expression<Action> first, params Expression<Action>[] more)
    {
        return new MustNotUseConstraint(subject, ResolvedMembers(subject, first, more));
    }

    /// <summary>
    ///     The subject must not construct any of the targets — a source-level object creation, <c>new</c>
    ///     included target-typed <c>new()</c> (GRAMMAR §5.3). Constructor-ness lives in the verb, so ordinary
    ///     selections name what may not be <c>new</c>ed; there is no expression overload (GRAMMAR §3.3).
    /// </summary>
    public static Constraint MustNotConstruct(this Selection subject, Selection first, params Selection[] more)
    {
        return new MustNotConstructConstraint(subject, Selections(subject, first, more));
    }

    /// <summary>The subject must not construct any of the targets (type sugar).</summary>
    public static Constraint MustNotConstruct(this Selection subject, Type first, params Type[] more)
    {
        return new MustNotConstructConstraint(subject, WrappedTypes(subject, first, more));
    }

    /// <summary>
    ///     The subject must not inject any of the targets — a source-level constructor-parameter dependency,
    ///     primary constructors included (GRAMMAR §5.3, §4.7). Injection-ness lives in the verb, so ordinary
    ///     selections name what may not be injected; the natural operands are the registration-fact selections
    ///     (<c>arch.Registered(Lifetime.Scoped)</c>), though any selection works. There is no expression
    ///     overload (GRAMMAR §3.3).
    /// </summary>
    public static Constraint MustNotInject(this Selection subject, Selection first, params Selection[] more)
    {
        return new MustNotInjectConstraint(subject, Selections(subject, first, more));
    }

    /// <summary>The subject must not inject any of the targets (type sugar).</summary>
    public static Constraint MustNotInject(this Selection subject, Type first, params Type[] more)
    {
        return new MustNotInjectConstraint(subject, WrappedTypes(subject, first, more));
    }

    /// <summary>
    ///     The subject must not catch any of the targets — a source-level <c>catch</c> clause whose caught
    ///     type resolves to a listed target, a bare <c>catch</c> counting as <c>System.Exception</c>
    ///     (GRAMMAR §5.3). Catch-ness lives in the verb, so ordinary selections name what may not be caught;
    ///     there is no expression overload (GRAMMAR §3.3).
    /// </summary>
    public static Constraint MustNotCatch(this Selection subject, Selection first, params Selection[] more)
    {
        return new MustNotCatchConstraint(subject, Selections(subject, first, more));
    }

    /// <summary>The subject must not catch any of the targets (type sugar).</summary>
    public static Constraint MustNotCatch(this Selection subject, Type first, params Type[] more)
    {
        return new MustNotCatchConstraint(subject, WrappedTypes(subject, first, more));
    }

    /// <summary>
    ///     The subject must not catch any of the targets in a <c>catch</c> clause that carries no
    ///     <c>when</c> filter — a bare <c>catch</c> counts as <c>System.Exception</c> and counts as
    ///     unfiltered, while a <c>catch when (…)</c> of any form is filtered (GRAMMAR §5.3). Filter
    ///     presence is syntactic: the filter's contents are never judged. Catch-ness lives in the verb,
    ///     so ordinary selections name what may not be caught unfiltered; there is no expression overload
    ///     (GRAMMAR §3.3).
    /// </summary>
    public static Constraint MustNotCatchUnfiltered(this Selection subject, Selection first, params Selection[] more)
    {
        return new MustNotCatchUnfilteredConstraint(subject, Selections(subject, first, more));
    }

    /// <summary>The subject must not catch any of the targets without a <c>when</c> filter (type sugar).</summary>
    public static Constraint MustNotCatchUnfiltered(this Selection subject, Type first, params Type[] more)
    {
        return new MustNotCatchUnfilteredConstraint(subject, WrappedTypes(subject, first, more));
    }

    /// <summary>
    ///     The subject must not swallow any of the targets — a <c>catch</c> clause whose caught type resolves
    ///     to a listed target, which carries no <c>when</c> filter, and whose block does not end in a
    ///     <c>throw</c> (GRAMMAR §5.3, §4.8). A bare <c>catch</c> counts as <c>System.Exception</c> and counts
    ///     as unfiltered; a filtered catch and a rethrowing catch are both lawful, so what the verb bans is
    ///     holding a failure and continuing. Both facts are syntactic: the filter's contents are never judged,
    ///     and the throw fact is the block's last statement, never an all-paths analysis. Catch-ness lives in
    ///     the verb, so ordinary selections name what may not be swallowed; there is no expression overload
    ///     (GRAMMAR §3.3).
    /// </summary>
    public static Constraint MustNotSwallow(this Selection subject, Selection first, params Selection[] more)
    {
        return new MustNotSwallowConstraint(subject, Selections(subject, first, more));
    }

    /// <summary>The subject must not swallow any of the targets (type sugar).</summary>
    public static Constraint MustNotSwallow(this Selection subject, Type first, params Type[] more)
    {
        return new MustNotSwallowConstraint(subject, WrappedTypes(subject, first, more));
    }

    /// <summary>
    ///     The subject must not expose any of the targets — a listed target appearing in a public signature
    ///     position (a return, parameter, or property/field/event type) of an effectively-public member
    ///     (GRAMMAR §5.3, §4.9). Exposure-ness lives in the verb, so ordinary selections name what may not
    ///     surface on the public API; there is no expression overload (GRAMMAR §3.3).
    /// </summary>
    public static Constraint MustNotExpose(this Selection subject, Selection first, params Selection[] more)
    {
        return new MustNotExposeConstraint(subject, Selections(subject, first, more));
    }

    /// <summary>The subject must not expose any of the targets (type sugar).</summary>
    public static Constraint MustNotExpose(this Selection subject, Type first, params Type[] more)
    {
        return new MustNotExposeConstraint(subject, WrappedTypes(subject, first, more));
    }

    /// <summary>
    ///     The subject must not throw any of the targets — the ban polarity beside the strict allow-list
    ///     <c>MustOnlyThrow</c>, for the case where the forbidden thrown types are enumerable and the
    ///     permitted ones are not (GRAMMAR §5.3). Matching is exact definition-level FQN, so a ban on
    ///     <c>Exception</c> deliberately does not reach derived throws. Throw-ness lives in the verb, so
    ///     ordinary selections name the forbidden thrown types; there is no expression overload
    ///     (GRAMMAR §3.3).
    /// </summary>
    public static Constraint MustNotThrow(this Selection subject, Selection first, params Selection[] more)
    {
        return new MustNotThrowConstraint(subject, Selections(subject, first, more));
    }

    /// <summary>The subject must not throw any of the targets (type sugar).</summary>
    public static Constraint MustNotThrow(this Selection subject, Type first, params Type[] more)
    {
        return new MustNotThrowConstraint(subject, WrappedTypes(subject, first, more));
    }

    /// <summary>
    ///     The subject may throw only the targets — STRICT: every thrown type, including BCL and external
    ///     exception types, must be in the allowed list (unlike <c>MustOnlyReference</c>, which exempts
    ///     external packages, GRAMMAR §4.1). Throw-ness lives in the verb, so ordinary selections name the
    ///     permitted thrown types; there is no expression overload (GRAMMAR §3.3).
    /// </summary>
    public static Constraint MustOnlyThrow(this Selection subject, Selection first, params Selection[] more)
    {
        return new MustOnlyThrowConstraint(subject, Selections(subject, first, more));
    }

    /// <summary>The subject may throw only the targets (type sugar).</summary>
    public static Constraint MustOnlyThrow(this Selection subject, Type first, params Type[] more)
    {
        return new MustOnlyThrowConstraint(subject, WrappedTypes(subject, first, more));
    }

    /// <summary>
    ///     The subject must belong to at least one of the memberships — the coverage verb, whose reds are
    ///     the subject types no membership names (GRAMMAR §5.3, §10). The memberships or-join in the
    ///     rendered sentence ("must belong to the Domain layer or the Web layer"), so the any-of reading is
    ///     stated by the sentence itself. There is deliberately no type sugar: memberships name where a
    ///     type may live — layers, projects, namespaces — and a bare <c>typeof</c> operand would degenerate
    ///     into "must be that type" (GRAMMAR §3.3).
    /// </summary>
    public static Constraint MustBelongTo(this Selection subject, Selection first, params Selection[] more)
    {
        return new MustBelongToConstraint(subject, Selections(subject, first, more));
    }

    /// <summary>
    ///     The subject must have exactly one counterpart whose name the template derives — the
    ///     correspondence verb, red on a subject with zero counterparts and red again with several
    ///     (GRAMMAR §5.3, §10). Every <c>{Name}</c> occurrence is replaced by the subject's simple name:
    ///     substitution is ordinal and case-sensitive, and a template with no <c>{Name}</c> at all fails at
    ///     spec build rather than checking a constant name. Matching is arity-free and nested types match
    ///     on leaf names. Deliberately one <c>among:</c> selection — a params list would reopen the ALL/ANY
    ///     question in a worse form — so authors union candidate homes with <c>arch.AnyOf</c>; and no
    ///     <c>Type</c> sugar, because this position derives a name rather than naming a type (GRAMMAR §10).
    ///     An <c>among:</c> selection matching nothing reds every subject — a false red, not a miss — and
    ///     raises no warning (GRAMMAR §4.7).
    /// </summary>
    public static Constraint MustHaveExactlyOneCounterpart(this Selection subject, Selection among, string named)
    {
        return new MustHaveExactlyOneCounterpartConstraint(
            Subject(subject), [NotNull(among, nameof(among))], NotNull(named, nameof(named)));
    }

    /// <summary>The subject must reside in a namespace glob.</summary>
    public static Constraint MustResideInNamespace(this Selection subject, string glob)
    {
        return new MustResideInNamespaceConstraint(Subject(subject), NotNull(glob, nameof(glob)));
    }

    /// <summary>
    ///     The subject must reside in the named project — declared-by membership (GRAMMAR §5.3, §4.1), so
    ///     any declarer of a type several projects compile satisfies it. Single-name arity mirrors
    ///     <see cref="MustResideInNamespace" />; several projects is <see cref="MustBelongTo" /> with
    ///     project memberships.
    /// </summary>
    public static Constraint MustResideInProject(this Selection subject, string projectName)
    {
        return new MustResideInProjectConstraint(Subject(subject), NotNull(projectName, nameof(projectName)));
    }

    /// <summary>The subject's type names must end with a suffix.</summary>
    public static Constraint MustHaveSuffix(this Selection subject, string suffix)
    {
        return new MustHaveSuffixConstraint(Subject(subject), NotNull(suffix, nameof(suffix)));
    }

    /// <summary>The subject's type names must start with a prefix.</summary>
    public static Constraint MustHavePrefix(this Selection subject, string prefix)
    {
        return new MustHavePrefixConstraint(Subject(subject), NotNull(prefix, nameof(prefix)));
    }

    /// <summary>The subject's type names must match a glob.</summary>
    public static Constraint MustHaveNameMatching(this Selection subject, string glob)
    {
        return new MustHaveNameMatchingConstraint(Subject(subject), NotNull(glob, nameof(glob)));
    }

    /// <summary>The subject must implement an interface.</summary>
    public static Constraint MustImplement(this Selection subject, Type type)
    {
        TypeAnchor anchor = TypeAnchor.FromType(NotNull(type, nameof(type)));
        return new MustImplementConstraint(Subject(subject), anchor);
    }

    /// <summary>
    ///     The subject must implement the interface named by string — the escape hatch for an interface
    ///     the spec project cannot compile against, so it need not take a package reference just to write
    ///     the <c>typeof</c>. <paramref name="interfaceFullName" /> is the interface <em>definition</em>'s
    ///     fully-qualified name as a report prints it, declared type-parameter names included
    ///     (<c>"MyApp.Web.IHandler&lt;T&gt;"</c>); it matches any construction of that definition, and a
    ///     constructed spelling matches nothing. Prefer <see cref="MustImplement(Selection,Type)" />
    ///     whenever the interface is referenceable — the compiler checks a <c>typeof</c>, and nothing
    ///     checks a string.
    /// </summary>
    public static Constraint MustImplement(this Selection subject, string interfaceFullName)
    {
        TypeAnchor anchor = TypeAnchor.FromName(NotNull(interfaceFullName, nameof(interfaceFullName)));
        return new MustImplementConstraint(Subject(subject), anchor);
    }

    /// <summary>The subject must derive from a base type.</summary>
    public static Constraint MustDeriveFrom(this Selection subject, Type type)
    {
        TypeAnchor anchor = TypeAnchor.FromType(NotNull(type, nameof(type)));
        return new MustDeriveFromConstraint(Subject(subject), anchor);
    }

    /// <summary>
    ///     The subject must derive from the base type named by string — the escape hatch for a base type
    ///     the spec project cannot compile against. <paramref name="baseTypeFullName" /> is the base
    ///     type <em>definition</em>'s fully-qualified name as a report prints it, matching any
    ///     construction of that definition; a constructed spelling matches nothing. Prefer
    ///     <see cref="MustDeriveFrom(Selection,Type)" /> whenever the base type is referenceable.
    /// </summary>
    public static Constraint MustDeriveFrom(this Selection subject, string baseTypeFullName)
    {
        TypeAnchor anchor = TypeAnchor.FromName(NotNull(baseTypeFullName, nameof(baseTypeFullName)));
        return new MustDeriveFromConstraint(Subject(subject), anchor);
    }

    /// <summary>The subject must carry an attribute.</summary>
    public static Constraint MustBeAttributedWith(this Selection subject, Type type)
    {
        TypeAnchor anchor = TypeAnchor.FromType(NotNull(type, nameof(type)));
        return new MustBeAttributedWithConstraint(Subject(subject), anchor);
    }

    /// <summary>
    ///     The subject must carry an attribute named by string — the escape hatch for an attribute the
    ///     spec project cannot compile against, so it need not take a package reference just to write the
    ///     <c>typeof</c>. <paramref name="attributeFullName" /> is the attribute <em>definition</em>'s
    ///     fully-qualified name in extraction format, <c>Attribute</c> suffix included
    ///     (<c>"ModelContextProtocol.Server.McpServerToolAttribute"</c>); it matches any construction of
    ///     that definition, and a constructed spelling matches nothing. Prefer
    ///     <see cref="MustBeAttributedWith(Selection,Type)" /> whenever the attribute is referenceable —
    ///     the compiler checks a <c>typeof</c>, and nothing checks a string.
    /// </summary>
    public static Constraint MustBeAttributedWith(this Selection subject, string attributeFullName)
    {
        TypeAnchor anchor = TypeAnchor.FromName(NotNull(attributeFullName, nameof(attributeFullName)));
        return new MustBeAttributedWithConstraint(Subject(subject), anchor);
    }

    /// <summary>
    ///     The subject must implement <typeparamref name="T" /> — <c>≡ MustImplement(typeof(T))</c>; an open generic
    ///     stays <c>typeof</c>.
    /// </summary>
    public static Constraint MustImplement<T>(this Selection subject)
    {
        return subject.MustImplement(typeof(T));
    }

    /// <summary>
    ///     The subject must derive from <typeparamref name="T" /> — <c>≡ MustDeriveFrom(typeof(T))</c>; an open generic
    ///     stays <c>typeof</c>.
    /// </summary>
    public static Constraint MustDeriveFrom<T>(this Selection subject)
    {
        return subject.MustDeriveFrom(typeof(T));
    }

    /// <summary>The subject must carry attribute <typeparamref name="T" /> — <c>≡ MustBeAttributedWith(typeof(T))</c>.</summary>
    public static Constraint MustBeAttributedWith<T>(this Selection subject)
        where T : Attribute
    {
        return subject.MustBeAttributedWith(typeof(T));
    }

    /// <summary>
    ///     The subject must not implement any of the interface anchors — none-of semantics (GRAMMAR §5.3, §10).
    ///     The negatives take <c>(Type first, params Type[] more)</c>: "must not implement `A` or `B`" is
    ///     unambiguous, unlike the single-<c>Type</c> positive.
    /// </summary>
    public static Constraint MustNotImplement(this Selection subject, Type first, params Type[] more)
    {
        return new MustNotImplementConstraint(Subject(subject), TypeAnchor.FromTypes(first, more));
    }

    /// <summary>
    ///     The subject must not implement any of the interface anchors named by string — none-of
    ///     semantics (GRAMMAR §5.3, §10) over the escape-hatch form, for interfaces the spec project
    ///     cannot compile against. Each name is an interface <em>definition</em>'s fully-qualified name as
    ///     a report prints it, declared type-parameter names included (<c>"MyApp.Web.IHandler&lt;T&gt;"</c>),
    ///     matching any construction of that definition; a constructed spelling matches nothing. The
    ///     overloads are homogeneous — one call is all <c>typeof</c> or all names; write a second rule to
    ///     mix them. Prefer <see cref="MustNotImplement(Selection,Type,Type[])" /> whenever the interfaces
    ///     are referenceable — the compiler checks a <c>typeof</c>, and nothing checks a string.
    /// </summary>
    public static Constraint MustNotImplement(this Selection subject, string first, params string[] more)
    {
        return new MustNotImplementConstraint(Subject(subject), TypeAnchor.FromNames(first, more));
    }

    /// <summary>The subject must not derive from any of the base-type anchors — none-of semantics (GRAMMAR §5.3, §10).</summary>
    public static Constraint MustNotDeriveFrom(this Selection subject, Type first, params Type[] more)
    {
        return new MustNotDeriveFromConstraint(Subject(subject), TypeAnchor.FromTypes(first, more));
    }

    /// <summary>
    ///     The subject must not derive from any of the base-type anchors named by string — none-of
    ///     semantics (GRAMMAR §5.3, §10) over the escape-hatch form, for base types the spec project
    ///     cannot compile against. Each name is a base type <em>definition</em>'s fully-qualified name as
    ///     a report prints it, matching any construction of that definition; a constructed spelling
    ///     matches nothing. The overloads are homogeneous. Prefer
    ///     <see cref="MustNotDeriveFrom(Selection,Type,Type[])" /> whenever the base types are
    ///     referenceable.
    /// </summary>
    public static Constraint MustNotDeriveFrom(this Selection subject, string first, params string[] more)
    {
        return new MustNotDeriveFromConstraint(Subject(subject), TypeAnchor.FromNames(first, more));
    }

    /// <summary>The subject must not be attributed with any of the attribute anchors — none-of semantics (GRAMMAR §5.3, §10).</summary>
    public static Constraint MustNotBeAttributedWith(this Selection subject, Type first, params Type[] more)
    {
        return new MustNotBeAttributedWithConstraint(Subject(subject), TypeAnchor.FromTypes(first, more));
    }

    /// <summary>
    ///     The subject must not be attributed with any of the attribute anchors named by string —
    ///     none-of semantics (GRAMMAR §5.3, §10) over the escape-hatch form, for attributes the spec
    ///     project cannot compile against. Each name is an attribute <em>definition</em>'s fully-qualified
    ///     name in extraction format, <c>Attribute</c> suffix included
    ///     (<c>"ModelContextProtocol.Server.McpServerToolAttribute"</c>), matching any construction of
    ///     that definition; a constructed spelling matches nothing. The overloads are homogeneous — one
    ///     call is all <c>typeof</c> or all names; write a second rule to mix them. Prefer
    ///     <see cref="MustNotBeAttributedWith(Selection,Type,Type[])" /> whenever the attributes are
    ///     referenceable — the compiler checks a <c>typeof</c>, and nothing checks a string.
    /// </summary>
    public static Constraint MustNotBeAttributedWith(this Selection subject, string first, params string[] more)
    {
        return new MustNotBeAttributedWithConstraint(Subject(subject), TypeAnchor.FromNames(first, more));
    }

    /// <summary>
    ///     The subject must not implement <typeparamref name="T" /> — <c>≡ MustNotImplement(typeof(T))</c>; an open
    ///     generic stays <c>typeof</c>.
    /// </summary>
    public static Constraint MustNotImplement<T>(this Selection subject)
    {
        return subject.MustNotImplement(typeof(T));
    }

    /// <summary>
    ///     The subject must not derive from <typeparamref name="T" /> — <c>≡ MustNotDeriveFrom(typeof(T))</c>; an open
    ///     generic stays <c>typeof</c>.
    /// </summary>
    public static Constraint MustNotDeriveFrom<T>(this Selection subject)
    {
        return subject.MustNotDeriveFrom(typeof(T));
    }

    /// <summary>The subject must not carry attribute <typeparamref name="T" /> — <c>≡ MustNotBeAttributedWith(typeof(T))</c>.</summary>
    public static Constraint MustNotBeAttributedWith<T>(this Selection subject)
        where T : Attribute
    {
        return subject.MustNotBeAttributedWith(typeof(T));
    }

    /// <summary>The subject must be sealed.</summary>
    public static Constraint MustBeSealed(this Selection subject)
    {
        return new MustBeSealedConstraint(Subject(subject));
    }

    /// <summary>The subject must be static.</summary>
    public static Constraint MustBeStatic(this Selection subject)
    {
        return new MustBeStaticConstraint(Subject(subject));
    }

    /// <summary>The subject must be abstract.</summary>
    public static Constraint MustBeAbstract(this Selection subject)
    {
        return new MustBeAbstractConstraint(Subject(subject));
    }

    /// <summary>The subject must be public.</summary>
    public static Constraint MustBePublic(this Selection subject)
    {
        return new MustBePublicConstraint(Subject(subject));
    }

    /// <summary>The subject must be internal.</summary>
    public static Constraint MustBeInternal(this Selection subject)
    {
        return new MustBeInternalConstraint(Subject(subject));
    }

    /// <summary>
    ///     The subject must be registered in a container — the completeness half of the DI axis beside
    ///     <see cref="MustNotInject(Selection,Selection,Selection[])" />'s shape half (GRAMMAR §5.3, §4.7).
    ///     Membership is exactly <c>arch.Registered()</c>'s, at any lifetime. Registration is read from
    ///     source-visible container registrations, so a registration the extraction cannot see — assembly
    ///     scanning, keyed overloads, a raw <c>ServiceDescriptor</c>, an extension compiled into a
    ///     package — makes a correctly registered type a false red; an estate that registers by convention
    ///     should not use the verb.
    /// </summary>
    public static Constraint MustBeRegistered(this Selection subject)
    {
        return new MustBeRegisteredConstraint(Subject(subject));
    }

    /// <summary>
    ///     The constraint-position escape hatch. The predicate is stored, never evaluated;
    ///     the required <paramref name="description" /> completes "must …". A blank
    ///     description fails spec build (validation §8 item 5).
    /// </summary>
    public static Constraint Must(this Selection subject, Func<ITypeInfo, bool> predicate, string description)
    {
        return new MustConstraint(Subject(subject), NotNull(predicate, nameof(predicate)), description);
    }

    private static IReadOnlyList<Selection> Selections(Selection subject, Selection first, Selection[] more)
    {
        NotNull(subject, nameof(subject));
        return OperandList.OneOrMore(first, more, selection => selection);
    }

    private static IReadOnlyList<Selection> WrappedTypes(Selection subject, Type first, Type[] more)
    {
        NotNull(subject, nameof(subject));
        return OperandList.OneOrMore(first, more, type => Wrap(subject, type));
    }

    private static IReadOnlyList<Member> Members(Selection subject, Member first, Member[] more)
    {
        NotNull(subject, nameof(subject));
        return OperandList.OneOrMore(first, more, member => member);
    }

    // The static-form MustNotUse sugar: each lambda resolves through MemberExpressionResolver stamped with
    // the subject's owner (the Wrap precedent), minting the identical Member leaf as arch.Member(() => ...).
    // Generic over the concrete lambda type so the two delegate-shape overloads (Func<object?> / Action)
    // share one body with no array-covariance conversion. Null/empty params edges mirror the
    // Members/WrappedTypes helpers exactly.
    private static IReadOnlyList<Member> ResolvedMembers<TLambda>(Selection subject, TLambda first, TLambda[] more)
        where TLambda : LambdaExpression
    {
        NotNull(subject, nameof(subject));
        Arch owner = subject.Owner;
        return OperandList.OneOrMore(first, more, lambda => MemberExpressionResolver.Resolve(owner, lambda));
    }

    // A bare type target wraps as a single-type selection stamped with the subject's owner, so the
    // sugar overload is exactly the selection overload with arch.Type(...) written for the caller.
    private static Selection Wrap(Selection subject, Type type)
    {
        return subject.Owner.Type(type);
    }

    private static Selection Subject(Selection subject)
    {
        return NotNull(subject, nameof(subject));
    }
}
