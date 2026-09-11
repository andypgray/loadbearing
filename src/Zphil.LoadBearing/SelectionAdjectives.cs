using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Model;
using static Zphil.LoadBearing.Internal.Guard;

namespace Zphil.LoadBearing;

/// <summary>
///     The v1 adjective vocabulary (GRAMMAR §5.2) as extension methods on <see cref="Selection" />.
/// </summary>
/// <remarks>
///     Each appends one closed-vocabulary adjective and returns a fresh selection re-stamped with
///     the same <see cref="Arch" /> owner. Selections are immutable values; each call yields a new
///     one, so a selection can be reused and refined in different directions.
/// </remarks>
public static class SelectionAdjectives
{
    /// <summary>Narrows to types declared in a namespace glob: " in `MyApp.Web.*`".</summary>
    public static Selection InNamespace(this Selection selection, string glob)
    {
        return Append(selection, new InNamespaceAdjective(NotNull(glob, nameof(glob))));
    }

    /// <summary>Narrows to a kind, substituting the subject head plural ("interfaces").</summary>
    public static Selection OfKind(this Selection selection, TypeKind kind)
    {
        return Append(selection, new OfKindAdjective(kind));
    }

    /// <summary>Narrows to types whose name ends with a suffix: " named `*Controller`".</summary>
    public static Selection WithSuffix(this Selection selection, string suffix)
    {
        return Append(selection, new WithSuffixAdjective(NotNull(suffix, nameof(suffix))));
    }

    /// <summary>Narrows to types whose name starts with a prefix: " named `Legacy*`".</summary>
    public static Selection WithPrefix(this Selection selection, string prefix)
    {
        return Append(selection, new WithPrefixAdjective(NotNull(prefix, nameof(prefix))));
    }

    /// <summary>
    ///     Narrows to types whose name matches a glob: " whose name matches `*Repo*`". <see cref="Named" />
    ///     is the exact form beside it.
    /// </summary>
    public static Selection WithNameMatching(this Selection selection, string glob)
    {
        return Append(selection, new WithNameMatchingAdjective(NotNull(glob, nameof(glob))));
    }

    /// <summary>
    ///     Narrows to the types named exactly: " named `Program`", or " named `A` or `B`" for several.
    ///     Ordinal and case-sensitive over the simple name — the name a report prints without namespace,
    ///     generic arity or containing type — so <c>Named("Line")</c> reaches a nested <c>Order.Line</c> and
    ///     <c>Named("Order.Line")</c> names nothing. A name reaches every type that carries it, in every
    ///     namespace and project. <see cref="WithNameMatching" /> is the glob form beside this one; the
    ///     <c>(first, more)</c> shape makes a zero-name call uncompilable.
    /// </summary>
    public static Selection Named(this Selection selection, string first, params string[] more)
    {
        NotNull(more, nameof(more));
        IReadOnlyList<string> names = OperandList.OneOrMore(first, more, name => name);
        return Append(selection, new NamedAdjective(names));
    }

    /// <summary>Narrows to types implementing an interface (open generic = any construction).</summary>
    public static Selection Implementing(this Selection selection, Type type)
    {
        TypeAnchor anchor = TypeAnchor.FromType(NotNull(type, nameof(type)));
        return Append(selection, new ImplementingAdjective(anchor));
    }

    /// <summary>
    ///     Narrows to types implementing the interface named by string — the escape hatch for an
    ///     interface the spec project cannot compile against, so it need not take a package reference
    ///     just to write the <c>typeof</c>. <paramref name="interfaceFullName" /> is the interface
    ///     <em>definition</em>'s fully-qualified name as a report prints it, declared type-parameter
    ///     names included (<c>"MyApp.Web.IHandler&lt;T&gt;"</c>); it matches any construction of that
    ///     definition, and a constructed spelling matches nothing. Prefer
    ///     <see cref="Implementing(Selection,Type)" /> whenever the interface is referenceable — the
    ///     compiler checks a <c>typeof</c>, and nothing checks a string.
    /// </summary>
    public static Selection Implementing(this Selection selection, string interfaceFullName)
    {
        TypeAnchor anchor = TypeAnchor.FromName(NotNull(interfaceFullName, nameof(interfaceFullName)));
        return Append(selection, new ImplementingAdjective(anchor));
    }

    /// <summary>Narrows to types derived from a base type: " derived from `ControllerBase`".</summary>
    public static Selection DerivedFrom(this Selection selection, Type type)
    {
        TypeAnchor anchor = TypeAnchor.FromType(NotNull(type, nameof(type)));
        return Append(selection, new DerivedFromAdjective(anchor));
    }

    /// <summary>
    ///     Narrows to types derived from the base type named by string — the escape hatch for a base
    ///     type the spec project cannot compile against. <paramref name="baseTypeFullName" /> is the
    ///     base type <em>definition</em>'s fully-qualified name as a report prints it
    ///     (<c>"Microsoft.AspNetCore.Mvc.ControllerBase"</c>), matching any construction of that
    ///     definition; a constructed spelling matches nothing. Prefer
    ///     <see cref="DerivedFrom(Selection,Type)" /> whenever the base type is referenceable.
    /// </summary>
    public static Selection DerivedFrom(this Selection selection, string baseTypeFullName)
    {
        TypeAnchor anchor = TypeAnchor.FromName(NotNull(baseTypeFullName, nameof(baseTypeFullName)));
        return Append(selection, new DerivedFromAdjective(anchor));
    }

    /// <summary>Narrows to types carrying an attribute: " attributed with `[ApiController]`".</summary>
    public static Selection AttributedWith(this Selection selection, Type type)
    {
        TypeAnchor anchor = TypeAnchor.FromType(NotNull(type, nameof(type)));
        return Append(selection, new AttributedWithAdjective(anchor));
    }

    /// <summary>
    ///     Narrows to types carrying an attribute named by string — the escape hatch for an attribute the
    ///     spec project cannot compile against, so it need not take a package reference just to write the
    ///     <c>typeof</c>. <paramref name="attributeFullName" /> is the attribute <em>definition</em>'s
    ///     fully-qualified name in extraction format, <c>Attribute</c> suffix included
    ///     (<c>"ModelContextProtocol.Server.McpServerToolAttribute"</c>); it matches any construction of
    ///     that definition, and a constructed spelling matches nothing. Prefer
    ///     <see cref="AttributedWith(Selection,Type)" /> whenever the attribute is referenceable — the
    ///     compiler checks a <c>typeof</c>, and nothing checks a string.
    /// </summary>
    public static Selection AttributedWith(this Selection selection, string attributeFullName)
    {
        TypeAnchor anchor = TypeAnchor.FromName(NotNull(attributeFullName, nameof(attributeFullName)));
        return Append(selection, new AttributedWithAdjective(anchor));
    }

    /// <summary>
    ///     Narrows to types implementing <typeparamref name="T" /> — <c>≡ Implementing(typeof(T))</c>; an open generic
    ///     stays <c>typeof</c>.
    /// </summary>
    public static Selection Implementing<T>(this Selection selection)
    {
        return selection.Implementing(typeof(T));
    }

    /// <summary>
    ///     Narrows to types derived from <typeparamref name="T" /> — <c>≡ DerivedFrom(typeof(T))</c>; an open generic
    ///     stays <c>typeof</c>.
    /// </summary>
    public static Selection DerivedFrom<T>(this Selection selection)
    {
        return selection.DerivedFrom(typeof(T));
    }

    /// <summary>Narrows to types carrying attribute <typeparamref name="T" /> — <c>≡ AttributedWith(typeof(T))</c>.</summary>
    public static Selection AttributedWith<T>(this Selection selection)
        where T : Attribute
    {
        return selection.AttributedWith(typeof(T));
    }

    /// <summary>
    ///     Excludes one or more selections — several are the union <c>arch.AnyOf</c> would mint, so
    ///     <c>.Except(a, b)</c> ≡ <c>.Except(arch.AnyOf(a, b))</c>. The clause renders sentence-final as a
    ///     parenthetical, ", except {ref}", that the sentence closes with a comma wherever text follows it
    ///     (GRAMMAR §5.2, §6).
    /// </summary>
    public static Selection Except(this Selection selection, Selection first, params Selection[] more)
    {
        NotNull(selection, nameof(selection));
        NotNull(more, nameof(more));
        IReadOnlyList<Selection> parts = OperandList.OneOrMore(first, more, exclusion => exclusion);
        Selection payload = ExceptPayload(selection.Owner, parts);
        return Append(selection, new ExceptAdjective(payload));
    }

    /// <summary>
    ///     Excludes one or more types — <c>≡ Except(arch.Type(a), arch.Type(b), …)</c>, the same sugar the
    ///     dependency verbs carry (GRAMMAR §3.3): identical model, identical prose.
    /// </summary>
    public static Selection Except(this Selection selection, Type first, params Type[] more)
    {
        NotNull(selection, nameof(selection));
        NotNull(more, nameof(more));
        Arch owner = selection.Owner;
        IReadOnlyList<Selection> parts = OperandList.OneOrMore(first, more, type => owner.Type(type));
        Selection payload = ExceptPayload(owner, parts);
        return Append(selection, new ExceptAdjective(payload));
    }

    /// <summary>
    ///     The selector-position escape hatch. The predicate is stored, never evaluated;
    ///     the required <paramref name="description" /> is what renders as a sentence-final relative
    ///     clause (GRAMMAR §5.6). A blank description fails spec build (validation §8 item 5).
    /// </summary>
    public static Selection Where(this Selection selection, Func<ITypeInfo, bool> predicate, string description)
    {
        return Append(selection, new WhereAdjective(NotNull(predicate, nameof(predicate)), description));
    }

    /// <summary>
    ///     Narrows to the types no source generator emitted, premodifying the subject head:
    ///     "authored types", "authored interfaces" (GRAMMAR §5.2, §6). This is the opt-out from the
    ///     §4.1 boundary that puts generator output inside a project noun — reach for it when a law a
    ///     generator's output cannot satisfy would otherwise fail on code nobody wrote.
    /// </summary>
    public static Selection Authored(this Selection selection)
    {
        return Append(selection, new AuthoredAdjective());
    }

    // The excluded set for one or more operands: a single operand passes through as the payload unchanged —
    // byte for byte the model a one-operand Except has always built — and several mint the union arch.AnyOf
    // would (GRAMMAR §5.1), so prose and evaluation both reach them by paths that already exist.
    private static Selection ExceptPayload(Arch owner, IReadOnlyList<Selection> parts)
    {
        return parts.Count == 1 ? parts[0] : UnionSelection.Create(owner, parts);
    }

    private static Selection Append(Selection selection, SelectionAdjective adjective)
    {
        NotNull(selection, nameof(selection));
        var adjectives = new List<SelectionAdjective>(selection.Adjectives) { adjective };

        // A union owns its adjectives rather than distributing them through its operands (GRAMMAR §5.1):
        // AnyOf(a, b).Except(c) is (a ∪ b) − c. Rebuilding it as a RefinedSelection would read the union's
        // Noun, which throws — the wall this arm removes.
        return selection is UnionSelection union
            ? union.WithAdjectives(adjectives)
            : new RefinedSelection(selection.Owner, selection.Noun, adjectives);
    }
}
