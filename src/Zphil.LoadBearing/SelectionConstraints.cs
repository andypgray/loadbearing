using System.Linq.Expressions;
using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing;

/// <summary>
///     The v1 modal-constraint vocabulary (GRAMMAR §5.3) as extension methods that turn a
///     <see cref="Selection" /> into a terminal <see cref="Constraint" />. Polarity is lexical —
///     negation lives in the verb name, never a <c>Not()</c> combinator (GRAMMAR §2). The four
///     dependency verbs carry both overloads (GRAMMAR §3.3): a <see cref="Selection" /> list and a
///     <see cref="Type" /> list (sugar that wraps each type as a single-type selection). The
///     <c>(first, params more)</c> shape makes a zero-target call uncompilable.
/// </summary>
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

    /// <summary>The subject must reside in a namespace glob.</summary>
    public static Constraint MustResideInNamespace(this Selection subject, string glob)
    {
        return new MustResideInNamespaceConstraint(Subject(subject), NotNull(glob, nameof(glob)));
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
        return new MustImplementConstraint(Subject(subject), NotNull(type, nameof(type)));
    }

    /// <summary>The subject must derive from a base type.</summary>
    public static Constraint MustDeriveFrom(this Selection subject, Type type)
    {
        return new MustDeriveFromConstraint(Subject(subject), NotNull(type, nameof(type)));
    }

    /// <summary>The subject must carry an attribute.</summary>
    public static Constraint MustBeAttributedWith(this Selection subject, Type type)
    {
        return new MustBeAttributedWithConstraint(Subject(subject), NotNull(type, nameof(type)));
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
        return new MustNotImplementConstraint(Subject(subject), AnchorTypes(first, more));
    }

    /// <summary>The subject must not derive from any of the base-type anchors — none-of semantics (GRAMMAR §5.3, §10).</summary>
    public static Constraint MustNotDeriveFrom(this Selection subject, Type first, params Type[] more)
    {
        return new MustNotDeriveFromConstraint(Subject(subject), AnchorTypes(first, more));
    }

    /// <summary>The subject must not be attributed with any of the attribute anchors — none-of semantics (GRAMMAR §5.3, §10).</summary>
    public static Constraint MustNotBeAttributedWith(this Selection subject, Type first, params Type[] more)
    {
        return new MustNotBeAttributedWithConstraint(Subject(subject), AnchorTypes(first, more));
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
        Guard.NotNull(subject, nameof(subject));
        var list = new List<Selection>(1 + more.Length) { NotNull(first, nameof(first)) };
        foreach (Selection selection in more) list.Add(NotNull(selection, nameof(more)));

        return list;
    }

    private static IReadOnlyList<Selection> WrappedTypes(Selection subject, Type first, Type[] more)
    {
        Guard.NotNull(subject, nameof(subject));
        var list = new List<Selection>(1 + more.Length) { Wrap(subject, NotNull(first, nameof(first))) };
        foreach (Type type in more) list.Add(Wrap(subject, NotNull(type, nameof(more))));

        return list;
    }

    // The raw-Type anchor list of a negative hierarchy verb (MustNotImplement / MustNotDeriveFrom /
    // MustNotBeAttributedWith): stored directly on the node (the hierarchy-verb shape, GRAMMAR §10), never
    // wrapped as selections. Null/empty-params edges mirror the WrappedTypes helper exactly.
    private static IReadOnlyList<Type> AnchorTypes(Type first, Type[] more)
    {
        var list = new List<Type>(1 + more.Length) { NotNull(first, nameof(first)) };
        foreach (Type type in more) list.Add(NotNull(type, nameof(more)));

        return list;
    }

    private static IReadOnlyList<Member> Members(Selection subject, Member first, Member[] more)
    {
        Guard.NotNull(subject, nameof(subject));
        var list = new List<Member>(1 + more.Length) { NotNull(first, nameof(first)) };
        foreach (Member member in more) list.Add(NotNull(member, nameof(more)));

        return list;
    }

    // The static-form MustNotUse sugar: each lambda resolves through MemberExpressionResolver stamped with
    // the subject's owner (the Wrap precedent), minting the identical Member leaf as arch.Member(() => ...).
    // Generic over the concrete lambda type so the two delegate-shape overloads (Func<object?> / Action)
    // share one body with no array-covariance conversion. Null/empty params edges mirror the
    // Members/WrappedTypes helpers exactly.
    private static IReadOnlyList<Member> ResolvedMembers<TLambda>(Selection subject, TLambda first, TLambda[] more)
        where TLambda : LambdaExpression
    {
        Guard.NotNull(subject, nameof(subject));
        Arch owner = subject.Owner;
        var list = new List<Member>(1 + more.Length) { MemberExpressionResolver.Resolve(owner, NotNull(first, nameof(first))) };
        foreach (TLambda lambda in more) list.Add(MemberExpressionResolver.Resolve(owner, NotNull(lambda, nameof(more))));

        return list;
    }

    // A bare type target wraps as a single-type selection stamped with the subject's owner, so the
    // sugar overload is exactly the selection overload with arch.Type(...) written for the caller.
    private static Selection Wrap(Selection subject, Type type)
    {
        return new RefinedSelection(subject.Owner, new TypeNoun(type), Array.Empty<SelectionAdjective>());
    }

    private static Selection Subject(Selection subject)
    {
        return Guard.NotNull(subject, nameof(subject));
    }

    private static T NotNull<T>(T value, string paramName)
        where T : class
    {
        return Guard.NotNull(value, paramName);
    }
}