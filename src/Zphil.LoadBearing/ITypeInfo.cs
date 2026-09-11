namespace Zphil.LoadBearing;

/// <summary>
///     What a type predicate sees: the facts of one type, handed to the lambda in
///     <c>.Where(type =&gt; ..., description)</c> and <c>.Must(type =&gt; ..., description)</c>. Every
///     fact is the type's declaration as C# writes it rather than what the compiler emits, so a static
///     class is neither sealed nor abstract, an interface is abstract, and a struct, enum or delegate
///     is sealed. A type from a referenced package or the framework carries only its identity and its
///     shape flags: its base type is null, and its interfaces, attributes and file paths are empty. The
///     lambda runs when the check runs, not when the spec is loaded; the description beside it is
///     required, and it is the description, never the lambda, that the generated agent context and the
///     check report print. New facts are added in later versions and the ones here keep their meaning.
/// </summary>
// Read by the predicate authors this contract exists for rather than through the interface
// in-solution, so solution-wide search finds only the implementations and calls the members unused.
// ReSharper disable UnusedMemberInSuper.Global
public interface ITypeInfo
{
    /// <summary>
    ///     The type's own name: no namespace, no containing type and no generic arity —
    ///     <c>Repository</c> for <c>Repository&lt;T&gt;</c>, and <c>Line</c> for a nested
    ///     <c>Order.Line</c>.
    /// </summary>
    string Name { get; }

    /// <summary>
    ///     The namespace declaring the type, dotted and without the type name: <c>MyApp.Domain</c> for
    ///     <c>MyApp.Domain.Order</c> and for its nested <c>Order.Line</c> alike. The empty string for a
    ///     type in the global namespace.
    /// </summary>
    string Namespace { get; }

    /// <summary>
    ///     Which kind of type this is. A record is reported as the class or struct it is;
    ///     <see cref="IsRecord" /> is what tells a record apart.
    /// </summary>
    TypeKind Kind { get; }

    /// <summary>
    ///     The name of the project declaring the type, or the assembly name for a type from a referenced
    ///     package or the framework.
    /// </summary>
    string ProjectName { get; }

    /// <summary>
    ///     The attributes written on the type itself, each as the attribute type's own facts — so a
    ///     predicate reads <c>attribute.Name</c> (<c>ApiControllerAttribute</c>, suffix intact) or its
    ///     namespace. Attributes a base type carries are not included, and the list is empty for a type
    ///     from a referenced package or the framework.
    /// </summary>
    IReadOnlyList<ITypeInfo> Attributes { get; }

    /// <summary>
    ///     The type's immediate base type. Null for an interface, for <see cref="object" />, and for a type
    ///     from a referenced package or the framework.
    /// </summary>
    ITypeInfo? BaseType { get; }

    /// <summary>
    ///     The interfaces the type implements directly. Empty for a type from a referenced package or the
    ///     framework.
    /// </summary>
    IReadOnlyList<ITypeInfo> Interfaces { get; }

    /// <summary>
    ///     The accessibility the declaration writes. A top-level type is
    ///     <see cref="Accessibility.Public" /> or <see cref="Accessibility.Internal" />, whichever the
    ///     declaration says or defaults to; the other four values reach only a nested type, and a nested
    ///     type with no modifier is <see cref="Accessibility.Private" />.
    /// </summary>
    Accessibility Accessibility { get; }

    /// <summary>
    ///     Whether the type is sealed as C# declares it: a static class reports <c>false</c>, and a struct,
    ///     enum or delegate reports <c>true</c> because its kind seals it.
    /// </summary>
    bool IsSealed { get; }

    /// <summary>
    ///     Whether the type is a static class. A static class reports <c>false</c> for both
    ///     <see cref="IsSealed" /> and <see cref="IsAbstract" />, so the three flags can be tested
    ///     independently.
    /// </summary>
    bool IsStatic { get; }

    /// <summary>
    ///     Whether the type is abstract as C# declares it: a static class reports <c>false</c>, and an
    ///     interface reports <c>true</c> because its kind makes it abstract.
    /// </summary>
    bool IsAbstract { get; }

    /// <summary>
    ///     Whether the type is a record class or a record struct. <see cref="Kind" /> reports the class or
    ///     struct underneath, so this is the flag a rule about records tests.
    /// </summary>
    bool IsRecord { get; }

    /// <summary>
    ///     Whether the type is generator output — the fact <c>Authored</c> filters out. True when
    ///     <c>System.CodeDom.Compiler.GeneratedCodeAttribute</c> sits on the type or on a type containing
    ///     it, so the nested types a generator emits inside an attributed container count too; or when
    ///     every file declaring the type is generator output, meaning the compiler reported the file as
    ///     generated or the file opens with an auto-generated banner comment. Every declaring file and not
    ///     merely one: a partial type with one part a person wrote is authored, so a violation reported
    ///     against it is one somebody can act on. Nothing is inferred from a file path, an <c>obj</c>
    ///     directory or a naming convention, and a hand-written type carrying the attribute is reported
    ///     generated — the signals are read as they are written.
    /// </summary>
    bool IsGenerated { get; }

    /// <summary>
    ///     The distinct paths of the files declaring the type (several for a partial type), as the
    ///     compilation recorded them, in declaration order. Empty for a type from a referenced package or
    ///     the framework.
    /// </summary>
    IReadOnlyList<string> FilePaths { get; }
}
