using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing;

/// <summary>
///     The read-only view of a declared member handed to member escape-hatch predicates
///     (<c>.Where(pred, ...)</c> and <c>.Must(pred, ...)</c> on a member selection, GRAMMAR §4.6,
///     §5.6). The member analog of <see cref="ITypeInfo" />: the v1 member-predicate input contract,
///     grown additively as extraction learns new facts. Predicates are stored on the model but never
///     evaluated at spec build — the mandatory description is what renders, not the lambda.
/// </summary>
// Same as ITypeInfo: read by predicate authors, not through the interface in-solution, so
// solution-wide search sees only the implementations and calls the members unused.
// ReSharper disable UnusedMemberInSuper.Global
public interface IMemberInfo
{
    /// <summary>The member's simple name (no declaring-type qualifier, no signature).</summary>
    string Name { get; }

    /// <summary>The kind of member (method, property, field, or event).</summary>
    MemberKind Kind { get; }

    /// <summary>
    ///     The type that declares the member, as the reused type-side contract — so a member
    ///     predicate can reach its declaring type's facts (GRAMMAR §4.6).
    /// </summary>
    ITypeInfo DeclaringType { get; }

    /// <summary>The declared accessibility.</summary>
    Accessibility Accessibility { get; }

    /// <summary>Whether the member is declared <c>static</c>.</summary>
    bool IsStatic { get; }

    /// <summary>
    ///     Whether the member is abstract, in C# declaration semantics: an <c>abstract</c> member and
    ///     every interface member report <c>true</c> (GRAMMAR §4.6).
    /// </summary>
    bool IsAbstract { get; }

    /// <summary>
    ///     Whether the member is virtual, in C# declaration semantics: a <c>virtual</c> member reports
    ///     <c>true</c>, but an <c>override</c> or <c>abstract</c> member reports <c>false</c> — an
    ///     override is not itself "virtual" in the authored sense (GRAMMAR §4.6).
    /// </summary>
    bool IsVirtual { get; }

    /// <summary>Whether the member is a method declared with the <c>async</c> keyword.</summary>
    bool IsAsync { get; }

    /// <summary>
    ///     The definition-level full name of a method's return type (<c>System.Void</c> for a void
    ///     method), or null for a non-method member. This is what a <c>.Returning</c> anchor matches
    ///     against (GRAMMAR §4.6).
    /// </summary>
    string? ReturnTypeFullName { get; }

    /// <summary>
    ///     The definition-level full name of a property/field/event member type, or null for a method.
    /// </summary>
    string? MemberTypeFullName { get; }

    /// <summary>
    ///     A method's declared parameters in declaration order, or an empty list for a
    ///     property/field/event member (never null). What a <c>MustAcceptParameter</c> anchor matches
    ///     against (GRAMMAR §4.6). Additive contract growth, the §5.6 discipline.
    /// </summary>
    IReadOnlyList<IParameterInfo> Parameters { get; }

    /// <summary>
    ///     The member's declared attributes, or an empty list when it declares none (never null). What a
    ///     member-side attribute adjective matches against (GRAMMAR §4.6). Declared-only: a property's
    ///     accessor attributes and a method's <c>[return:]</c> attributes hang off other symbols and are
    ///     outside this fact. Additive contract growth, the §5.6 discipline.
    /// </summary>
    IReadOnlyList<IAttributeInfo> Attributes { get; }

    /// <summary>
    ///     Whether a property declares a setter accessor of any kind — a <c>private set</c>, an
    ///     <c>internal set</c> and an <c>init</c> all report <c>true</c> — and <c>false</c> for every
    ///     non-property member (GRAMMAR §4.6). Accessibility-blind: the fact is that a setter exists, not
    ///     that a caller outside the type can reach it. Declaration shape, not deep immutability: a property
    ///     with no setter still hands back a value the caller may mutate. Additive contract growth, the §5.6
    ///     discipline.
    /// </summary>
    bool HasSetter { get; }

    /// <summary>
    ///     Whether a property's setter is <c>init</c>-only, and <c>false</c> for every non-property member
    ///     and for a plain <c>set</c> (GRAMMAR §4.6). Refines <see cref="HasSetter" /> rather than competing
    ///     with it — an init-only setter is a setter, so this implies it. Recorded as its own fact so the
    ///     setter's kind is available to a predicate even where no verb reads it. Additive contract growth,
    ///     the §5.6 discipline.
    /// </summary>
    bool HasInitOnlySetter { get; }

    /// <summary>
    ///     Whether a field is declared <c>readonly</c>, and <c>false</c> for every non-field member and for a
    ///     <c>const</c> field — the two are disjoint declarations, not a refinement (GRAMMAR §4.6). The
    ///     casing is deliberate and will not match a verb spelling the same word: a fact name mirrors its
    ///     source, here <c>IFieldSymbol.IsReadOnly</c>, the symbol API extraction reads, where a verb name
    ///     mirrors the fragment it renders and so the C# keyword. Declaration shape, not deep immutability:
    ///     a readonly field of a mutable type is still mutated through it. Additive contract growth, the
    ///     §5.6 discipline.
    /// </summary>
    bool IsReadOnly { get; }

    /// <summary>
    ///     Whether a field is declared <c>const</c>, and <c>false</c> for every non-field member
    ///     (GRAMMAR §4.6). A const field is also <see cref="IsStatic" />. Additive contract growth, the §5.6
    ///     discipline.
    /// </summary>
    bool IsConst { get; }

    /// <summary>The distinct file paths declaring the member, verbatim as compiled.</summary>
    IReadOnlyList<string> FilePaths { get; }
}
