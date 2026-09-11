using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Model;
using static Zphil.LoadBearing.Internal.Guard;

namespace Zphil.LoadBearing;

/// <summary>
///     The adjectives that narrow a <see cref="Selection" />. Each returns a new selection carrying
///     one more condition and leaves the original untouched, so one selection can be assigned to a
///     variable and refined in several directions. Chain as many as you like, then finish with a
///     <c>Must</c> verb, or project to members with <c>Members</c>, <c>Methods</c>,
///     <c>Properties</c>, <c>Fields</c> or <c>Events</c>.
/// </summary>
// Every method here funnels through Append: one adjective onto the ordered list, the same Arch owner
// re-stamped, a fresh selection each time — which is what sentence assembly reads (GRAMMAR §5.2, §6).
public static class SelectionAdjectives
{
    /// <summary>
    ///     Narrows the selection to the types whose namespace matches a glob, such as
    ///     <c>arch.Types.InNamespace("MyApp.Web.*")</c>. Matching is by dot-separated segment and
    ///     case-sensitive: a trailing <c>.*</c> covers the namespace itself and everything beneath it, a
    ///     <c>*</c> standing alone as a segment matches exactly one segment, a <c>*</c> inside a segment
    ///     (<c>MyApp.Legacy*</c>) matches within that segment only, and a lone <c>*</c> matches every
    ///     namespace. A blank glob, or one whose text before a trailing <c>.*</c> itself contains a
    ///     <c>*</c> (which could never match), is reported when the spec is loaded.
    /// </summary>
    public static Selection InNamespace(this Selection selection, string glob)
    {
        return Append(selection, new InNamespaceAdjective(NotNull(glob, nameof(glob))));
    }

    /// <summary>
    ///     Narrows the selection to the types of one kind, such as
    ///     <c>arch.Types.OfKind(TypeKind.Interface)</c>. The kinds are class, interface, struct, enum and
    ///     delegate. There is no record kind: select records with <see cref="Where" /> over
    ///     <c>IsRecord</c> on <see cref="ITypeInfo" />.
    /// </summary>
    public static Selection OfKind(this Selection selection, TypeKind kind)
    {
        return Append(selection, new OfKindAdjective(kind));
    }

    /// <summary>
    ///     Narrows the selection to the types whose name ends with a suffix, such as
    ///     <c>arch.Types.WithSuffix("Controller")</c>. The comparison is case-sensitive and literal over
    ///     the type's simple name — the name a report prints, without namespace, generic arity or
    ///     containing type. Literal means the suffix is not a glob: a <c>*</c> in it matches a <c>*</c>,
    ///     so over a codebase declaring <c>OrderController</c>, <c>WithSuffix("*Controller")</c> selects
    ///     nothing while <c>WithNameMatching("*Controller")</c> selects it. A blank suffix is reported
    ///     when the spec is loaded. <see cref="WithNameMatching" /> is the glob form beside this one.
    /// </summary>
    public static Selection WithSuffix(this Selection selection, string suffix)
    {
        return Append(selection, new WithSuffixAdjective(NotNull(suffix, nameof(suffix))));
    }

    /// <summary>
    ///     Narrows the selection to the types whose name starts with a prefix, such as
    ///     <c>arch.Types.WithPrefix("Legacy")</c>. The comparison is case-sensitive and literal over the
    ///     type's simple name — the name a report prints, without namespace, generic arity or containing
    ///     type. Literal means the prefix is not a glob: a <c>*</c> in it matches a <c>*</c>, so over a
    ///     codebase declaring <c>LegacyBilling</c>, <c>WithPrefix("Legacy*")</c> selects nothing while
    ///     <c>WithNameMatching("Legacy*")</c> selects it. A blank prefix is reported when the spec is
    ///     loaded. <see cref="WithNameMatching" /> is the glob form beside this one.
    /// </summary>
    public static Selection WithPrefix(this Selection selection, string prefix)
    {
        return Append(selection, new WithPrefixAdjective(NotNull(prefix, nameof(prefix))));
    }

    /// <summary>
    ///     Narrows the selection to the types whose name matches a glob, such as
    ///     <c>arch.Types.WithNameMatching("*Repo*")</c>. Matching is case-sensitive over the type's
    ///     simple name — the name a report prints, without namespace, generic arity or containing type:
    ///     <c>*</c> matches any run of characters including none, every other character matches itself, a
    ///     pattern with no <c>*</c> is an exact name match, and a lone <c>*</c> matches every name. A name
    ///     is one token here, so there is none of the dot-segment structure a namespace glob has and no
    ///     subtree operator. A blank glob is reported when the spec is loaded, and
    ///     <see cref="Named" /> is the exact form beside this one.
    /// </summary>
    public static Selection WithNameMatching(this Selection selection, string glob)
    {
        return Append(selection, new WithNameMatchingAdjective(NotNull(glob, nameof(glob))));
    }

    /// <summary>
    ///     Narrows the selection to the types with exactly these names, such as
    ///     <c>arch.Types.Named("Program")</c> or <c>arch.Types.Named("Order", "Invoice")</c>, which keeps
    ///     the types carrying either name. The comparison is case-sensitive over the type's simple name —
    ///     the name a report prints, without namespace, generic arity or containing type — so
    ///     <c>Named("Line")</c> reaches a nested <c>Order.Line</c>, <c>Named("Order.Line")</c> names
    ///     nothing, and <c>Named("Repository")</c> reaches <c>Repository&lt;T&gt;</c>. A name reaches
    ///     every type that carries it, in every namespace and project, and a <c>*</c> in it is a literal
    ///     character; <see cref="WithNameMatching" /> is the glob form beside this one. At least one name
    ///     is required, and a blank one is reported when the spec is loaded.
    /// </summary>
    public static Selection Named(this Selection selection, string first, params string[] more)
    {
        NotNull(more, nameof(more));
        IReadOnlyList<string> names = OperandList.OneOrMore(first, more, name => name);
        return Append(selection, new NamedAdjective(names));
    }

    /// <summary>
    ///     Narrows the selection to the types implementing an interface, such as
    ///     <c>arch.Types.Implementing(typeof(IHandler&lt;&gt;))</c>. The whole interface set is read,
    ///     with type arguments substituted, so an interface reached through a base class or through
    ///     another interface counts: a class extending <c>HandlerBase&lt;Order&gt;</c> where
    ///     <c>HandlerBase&lt;T&gt; : IHandler&lt;T&gt;</c> matches <c>typeof(IHandler&lt;Order&gt;)</c>.
    ///     Pass the open definition, <c>typeof(IHandler&lt;&gt;)</c>, to match every construction, or a
    ///     constructed type to match that one exactly. The interface itself may come from anywhere, but a
    ///     type from a referenced package or the framework is never selected, because only the types the
    ///     solution declares carry the interface information this reads. Nothing checks that the type is
    ///     an interface: given a class it simply selects nothing, and a rule whose subject selects nothing
    ///     fails the check.
    /// </summary>
    public static Selection Implementing(this Selection selection, Type type)
    {
        TypeAnchor anchor = TypeAnchor.FromType(NotNull(type, nameof(type)));
        return Append(selection, new ImplementingAdjective(anchor));
    }

    /// <summary>
    ///     Narrows the selection to the types implementing the interface with this name — the form to use
    ///     for an interface the spec project cannot compile against, so it need not take a package
    ///     reference just to write the <c>typeof</c>. <paramref name="interfaceFullName" /> is the
    ///     interface definition's fully-qualified name as a report prints it, declared type-parameter
    ///     names included (<c>"MyApp.Web.IHandler&lt;T&gt;"</c>); it matches every construction of that
    ///     definition, and a constructed spelling such as
    ///     <c>"MyApp.Web.IHandler&lt;MyApp.Order&gt;"</c> matches nothing. The whole interface set is
    ///     read, so an interface reached through a base class or through another interface counts, and a
    ///     type from a referenced package or the framework is never selected. Blankness is the only thing
    ///     checked, and a blank name is reported when the spec is loaded: a misspelt name is a legal name
    ///     that selects nothing. Prefer the <see cref="System.Type" /> overload whenever the interface is
    ///     referenceable, because the compiler checks a <c>typeof</c> and nothing checks a string.
    /// </summary>
    public static Selection Implementing(this Selection selection, string interfaceFullName)
    {
        TypeAnchor anchor = TypeAnchor.FromName(NotNull(interfaceFullName, nameof(interfaceFullName)));
        return Append(selection, new ImplementingAdjective(anchor));
    }

    /// <summary>
    ///     Narrows the selection to the types derived from a base type, such as
    ///     <c>arch.Types.DerivedFrom(typeof(ControllerBase))</c>. The whole base-type chain is read, with
    ///     type arguments substituted, so a type derived through intermediate bases counts. Pass an open
    ///     definition, <c>typeof(HandlerBase&lt;&gt;)</c>, to match every construction, or a constructed
    ///     type to match that one exactly. The base type itself may come from anywhere, but a type from a
    ///     referenced package or the framework is never selected, because only the types the solution
    ///     declares carry the base-type information this reads. Nothing checks that the type is not an
    ///     interface: given an interface it simply selects nothing, and a rule whose subject selects
    ///     nothing fails the check.
    /// </summary>
    public static Selection DerivedFrom(this Selection selection, Type type)
    {
        TypeAnchor anchor = TypeAnchor.FromType(NotNull(type, nameof(type)));
        return Append(selection, new DerivedFromAdjective(anchor));
    }

    /// <summary>
    ///     Narrows the selection to the types derived from the base type with this name — the form to use
    ///     for a base type the spec project cannot compile against, so it need not take a package
    ///     reference just to write the <c>typeof</c>. <paramref name="baseTypeFullName" /> is the base
    ///     type definition's fully-qualified name as a report prints it, declared type-parameter names
    ///     included (<c>"Microsoft.AspNetCore.Mvc.ControllerBase"</c>,
    ///     <c>"MyApp.Web.HandlerBase&lt;T&gt;"</c>); it matches every construction of that definition, and
    ///     a constructed spelling matches nothing. The whole base-type chain is read, so a type derived
    ///     through intermediate bases counts, an intermediate base from a package included, while a type
    ///     from a referenced package or the framework is never itself selected. Blankness is the only
    ///     thing checked, and a blank name is reported when the spec is loaded: a misspelt name is a legal
    ///     name that selects nothing. Prefer the <see cref="System.Type" /> overload whenever the base
    ///     type is referenceable, because the compiler checks a <c>typeof</c> and nothing checks a string.
    /// </summary>
    public static Selection DerivedFrom(this Selection selection, string baseTypeFullName)
    {
        TypeAnchor anchor = TypeAnchor.FromName(NotNull(baseTypeFullName, nameof(baseTypeFullName)));
        return Append(selection, new DerivedFromAdjective(anchor));
    }

    /// <summary>
    ///     Narrows the selection to the types carrying an attribute, such as
    ///     <c>arch.Types.AttributedWith(typeof(ApiControllerAttribute))</c>. Attributes written on the
    ///     type itself count and no others, so an attribute a base type carries does not. Pass an open
    ///     generic definition to match every construction of it, or a constructed attribute type to match
    ///     that one exactly. The attribute itself may come from anywhere, but a type from a referenced
    ///     package or the framework is never selected, because only the types the solution declares carry
    ///     the attribute information this reads. Nothing checks that the type is an attribute: given
    ///     anything else it simply selects nothing, and a rule whose subject selects nothing fails the
    ///     check.
    /// </summary>
    public static Selection AttributedWith(this Selection selection, Type type)
    {
        TypeAnchor anchor = TypeAnchor.FromType(NotNull(type, nameof(type)));
        return Append(selection, new AttributedWithAdjective(anchor));
    }

    /// <summary>
    ///     Narrows the selection to the types carrying the attribute with this name — the form to use for
    ///     an attribute the spec project cannot compile against, so it need not take a package reference
    ///     just to write the <c>typeof</c>. <paramref name="attributeFullName" /> is the attribute
    ///     definition's fully-qualified name with the <c>Attribute</c> suffix included
    ///     (<c>"ModelContextProtocol.Server.McpServerToolAttribute"</c>); it matches every construction of
    ///     that definition, and a constructed spelling matches nothing. Attributes written on the type
    ///     itself count and no others, and a type from a referenced package or the framework is never
    ///     selected. Blankness is the only thing checked, and a blank name is reported when the spec is
    ///     loaded: a misspelt name is a legal name that selects nothing. Prefer the
    ///     <see cref="System.Type" /> overload whenever the attribute is referenceable, because the
    ///     compiler checks a <c>typeof</c> and nothing checks a string.
    /// </summary>
    public static Selection AttributedWith(this Selection selection, string attributeFullName)
    {
        TypeAnchor anchor = TypeAnchor.FromName(NotNull(attributeFullName, nameof(attributeFullName)));
        return Append(selection, new AttributedWithAdjective(anchor));
    }

    /// <summary>
    ///     Narrows the selection to the types implementing <typeparamref name="T" />, such as
    ///     <c>arch.Types.Implementing&lt;IHandler&gt;()</c>. The whole interface set is read, with type
    ///     arguments substituted, so an interface reached through a base class or through another
    ///     interface counts, while a type from a referenced package or the framework is never itself
    ///     selected. An open generic has no type-argument form: for <c>IHandler&lt;&gt;</c> use the
    ///     <see cref="System.Type" /> overload and pass <c>typeof(IHandler&lt;&gt;)</c>, which matches
    ///     every construction of it. Nothing checks that <typeparamref name="T" /> is an interface: given
    ///     a class it simply selects nothing, and a rule whose subject selects nothing fails the check.
    /// </summary>
    public static Selection Implementing<T>(this Selection selection)
    {
        return selection.Implementing(typeof(T));
    }

    /// <summary>
    ///     Narrows the selection to the types derived from <typeparamref name="T" />, such as
    ///     <c>arch.Types.DerivedFrom&lt;ControllerBase&gt;()</c>. The whole base-type chain is read, with
    ///     type arguments substituted, so a type derived through intermediate bases counts, while a type
    ///     from a referenced package or the framework is never itself selected. An open generic has no
    ///     type-argument form: for <c>HandlerBase&lt;&gt;</c> use the <see cref="System.Type" /> overload
    ///     and pass <c>typeof(HandlerBase&lt;&gt;)</c>, which matches every construction of it. Nothing
    ///     checks that <typeparamref name="T" /> is not an interface: given an interface it simply selects
    ///     nothing, and a rule whose subject selects nothing fails the check.
    /// </summary>
    public static Selection DerivedFrom<T>(this Selection selection)
    {
        return selection.DerivedFrom(typeof(T));
    }

    /// <summary>
    ///     Narrows the selection to the types carrying attribute <typeparamref name="T" />, such as
    ///     <c>arch.Types.AttributedWith&lt;ApiControllerAttribute&gt;()</c>. Attributes written on the
    ///     type itself count and no others, so an attribute a base type carries does not, and a type from
    ///     a referenced package or the framework is never selected. An open generic attribute has no
    ///     type-argument form: for <c>MarkAttribute&lt;&gt;</c> use the <see cref="System.Type" />
    ///     overload and pass <c>typeof(MarkAttribute&lt;&gt;)</c>, which matches every construction of it.
    /// </summary>
    public static Selection AttributedWith<T>(this Selection selection)
        where T : Attribute
    {
        return selection.AttributedWith(typeof(T));
    }

    /// <summary>
    ///     Narrows the selection by removing every type the given selections name, such as
    ///     <c>arch.Namespace("MyApp.Web.*").Except(arch.Types.WithSuffix("Dto"))</c>. Several exclusions
    ///     remove the types any one of them names, and at least one exclusion is required. The exclusion
    ///     is rendered last in the rule's sentence in the generated agent context, after any
    ///     <see cref="Where" /> on the same selection.
    /// </summary>
    public static Selection Except(this Selection selection, Selection first, params Selection[] more)
    {
        NotNull(selection, nameof(selection));
        NotNull(more, nameof(more));
        return AppendExcept(selection, OperandList.OneOrMore(first, more, exclusion => exclusion));
    }

    /// <summary>
    ///     Narrows the selection by removing the listed types, such as
    ///     <c>arch.Types.WithSuffix("Controller").Except(typeof(HomeController))</c>. Types from
    ///     referenced packages and the framework are accepted, and a generic type is given as its open
    ///     definition. At least one type is required. To remove types and other selections in one call,
    ///     wrap each type in <c>arch.Type</c> and use the <see cref="Selection" /> overload.
    /// </summary>
    public static Selection Except(this Selection selection, Type first, params Type[] more)
    {
        NotNull(selection, nameof(selection));
        NotNull(more, nameof(more));
        Arch owner = selection.Owner;
        return AppendExcept(selection, OperandList.OneOrMore(first, more, type => owner.Type(type)));
    }

    /// <summary>
    ///     Narrows the selection with a predicate of your own, for what the adjectives cannot say:
    ///     <c>arch.Types.Where(t =&gt; t.IsRecord, description: "that are records")</c>. The predicate
    ///     reads the facts on <see cref="ITypeInfo" /> and runs against each candidate type when the check
    ///     runs, never when the spec is loaded. <paramref name="description" /> is required and completes
    ///     the subject as a relative clause ("that are records"); it is rendered verbatim in the generated
    ///     agent context in place of the wording an adjective would produce, last in the clause list, and
    ///     nothing checks that it describes what the predicate does. A blank or multi-line description is
    ///     reported when the spec is loaded.
    /// </summary>
    public static Selection Where(this Selection selection, Func<ITypeInfo, bool> predicate, string description)
    {
        return Append(selection, new WhereAdjective(NotNull(predicate, nameof(predicate)), description));
    }

    /// <summary>
    ///     Narrows the selection to the types no source generator emitted, such as
    ///     <c>arch.Project("MyApp.Web").Authored()</c>. A project selection holds generated types unless
    ///     you narrow it this way, so reach for this when a rule a generator's output could never satisfy
    ///     would otherwise fail on code nobody wrote. A type counts as generated when
    ///     <c>[GeneratedCode]</c> sits on it or on a type containing it, or when every file declaring it
    ///     is generator output — a document the compiler generated, or a file whose leading comment
    ///     carries the <c>&lt;auto-generated&gt;</c> banner. Nothing is inferred from a file path or an
    ///     <c>obj</c> directory. The same fact is <c>IsGenerated</c> on <see cref="ITypeInfo" />, so
    ///     <see cref="Where" /> can combine it with anything else.
    /// </summary>
    public static Selection Authored(this Selection selection)
    {
        return Append(selection, new AuthoredAdjective());
    }

    // The excluded set for one or more operands: a single operand passes through as the payload unchanged —
    // byte for byte the model a one-operand Except has always built — and several mint the union arch.AnyOf
    // would (GRAMMAR §5.1), so prose and evaluation both reach them by paths that already exist.
    private static Selection AppendExcept(Selection selection, IReadOnlyList<Selection> exclusions)
    {
        Selection payload = exclusions.Count == 1 ? exclusions[0] : UnionSelection.Create(selection.Owner, exclusions);
        return Append(selection, new ExceptAdjective(payload));
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
