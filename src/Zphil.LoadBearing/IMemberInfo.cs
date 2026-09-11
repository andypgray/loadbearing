using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing;

/// <summary>
///     What a member predicate sees: the facts of one declared member, handed to the lambda in
///     <c>.Where(member =&gt; ..., description)</c> and <c>.Must(member =&gt; ..., description)</c> on a
///     member selection. Every fact is the member's declaration as C# writes it rather than what the
///     compiler emits, so an <c>override</c> member is not virtual, an interface member is abstract,
///     and a property with an <c>init</c> setter has a setter. Facts that belong to one kind of member
///     are simply false or null on the others, so a predicate may read any of them without testing
///     <see cref="Kind" /> first. The lambda runs when the check runs, not when the spec is loaded; the
///     description beside it is required, and it is the description, never the lambda, that the
///     generated agent context and the check report print. New facts are added in later versions and
///     the ones here keep their meaning.
/// </summary>
// Same as ITypeInfo: read by predicate authors, not through the interface in-solution, so
// solution-wide search sees only the implementations and calls the members unused.
// ReSharper disable UnusedMemberInSuper.Global
public interface IMemberInfo
{
    /// <summary>
    ///     The member's own name, with no declaring type in front of it and no signature after it:
    ///     <c>Handle</c> for <c>OrderService.Handle(Order)</c>, and <c>Echo</c> for a generic
    ///     <c>Echo&lt;T&gt;</c>.
    /// </summary>
    string Name { get; }

    /// <summary>Which kind of member this is: a method, a property, a field or an event.</summary>
    MemberKind Kind { get; }

    /// <summary>
    ///     The type that declares the member, carrying the same facts a type predicate sees — so one lambda
    ///     can test the member and its declaring type's name, namespace, attributes or interfaces together.
    /// </summary>
    ITypeInfo DeclaringType { get; }

    /// <summary>
    ///     The accessibility the declaration writes. A member with no modifier reports what C# gives it:
    ///     <see cref="Accessibility.Private" /> in a class or struct, <see cref="Accessibility.Public" />
    ///     in an interface.
    /// </summary>
    Accessibility Accessibility { get; }

    /// <summary>Whether the declaration says <c>static</c>. A <c>const</c> field reports <c>true</c>.</summary>
    bool IsStatic { get; }

    /// <summary>
    ///     Whether the member is abstract as C# declares it: an <c>abstract</c> member reports
    ///     <c>true</c>, and so does every interface member.
    /// </summary>
    bool IsAbstract { get; }

    /// <summary>
    ///     Whether the declaration says <c>virtual</c>. An <c>override</c> or an <c>abstract</c> member
    ///     reports <c>false</c>: neither is itself virtual in the sense the author wrote.
    /// </summary>
    bool IsVirtual { get; }

    /// <summary>
    ///     Whether a method's declaration says <c>async</c>, and <c>false</c> for a property, field or
    ///     event.
    /// </summary>
    bool IsAsync { get; }

    /// <summary>
    ///     A method's return type as a full name — namespace and containing types included, and a generic
    ///     spelled with its declared type-parameter names
    ///     (<c>System.Threading.Tasks.Task&lt;TResult&gt;</c>), so every construction of one generic type
    ///     reads the same. <c>System.Void</c> for a method that returns nothing, and null for a property,
    ///     field or event.
    /// </summary>
    string? ReturnTypeFullName { get; }

    /// <summary>
    ///     A property's, field's or event's type as a full name, in the same form
    ///     <see cref="ReturnTypeFullName" /> uses. Null for a method: exactly one of the two is set.
    /// </summary>
    string? MemberTypeFullName { get; }

    /// <summary>
    ///     A method's declared parameters in declaration order; empty for a property, field or event, and
    ///     never null. A default value does not change the list, an extension method's <c>this</c>
    ///     parameter is in it, and <c>ref</c>, <c>in</c> and <c>out</c> do not change the recorded type. A
    ///     record's positional parameters are not here — they surface as the properties they generate.
    /// </summary>
    IReadOnlyList<IParameterInfo> Parameters { get; }

    /// <summary>
    ///     The attributes written on the member itself, ordered by full name; empty when there are none,
    ///     and never null. Attributes on a property's <c>get</c> or <c>set</c>, and <c>[return:]</c>
    ///     attributes, belong to other declarations and are not here. A partial method reports both parts'
    ///     attributes together.
    /// </summary>
    IReadOnlyList<IAttributeInfo> Attributes { get; }

    /// <summary>
    ///     Whether a property declares a setter of any kind: a <c>private set</c>, an <c>internal set</c>
    ///     and an <c>init</c> all report <c>true</c>, and every other kind of member reports <c>false</c>.
    ///     It reports what the declaration has, not what a caller outside the type can reach, and not
    ///     whether the value a get-only property hands back can itself be mutated.
    /// </summary>
    bool HasSetter { get; }

    /// <summary>
    ///     Whether a property's setter is an <c>init</c> accessor, and <c>false</c> for a plain <c>set</c>
    ///     and for every other kind of member. An init-only setter is still a setter, so
    ///     <see cref="HasSetter" /> is <c>true</c> wherever this is.
    /// </summary>
    bool HasInitOnlySetter { get; }

    /// <summary>
    ///     Whether a field declares <c>readonly</c>, and <c>false</c> for a <c>const</c> field — a
    ///     different declaration rather than a stricter one — and for every other kind of member. It
    ///     reports the declaration: a readonly field holding a mutable object can still be written through.
    /// </summary>
    // The casing is deliberate: a fact name mirrors the symbol API it is read from, IFieldSymbol.IsReadOnly,
    // where a verb name mirrors the fragment it renders and so the C# keyword (MustBeReadonly). Do not
    // align the two.
    bool IsReadOnly { get; }

    /// <summary>
    ///     Whether a field declares <c>const</c>, and <c>false</c> for every other kind of member. A const
    ///     field also reports <see cref="IsStatic" />.
    /// </summary>
    bool IsConst { get; }

    /// <summary>
    ///     The distinct paths of the files declaring the member, as the compilation recorded them. A
    ///     partial method declared in two files reports both.
    /// </summary>
    IReadOnlyList<string> FilePaths { get; }
}
