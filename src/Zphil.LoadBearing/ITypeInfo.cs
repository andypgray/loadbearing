namespace Zphil.LoadBearing;

/// <summary>
///     The read-only view of a type handed to escape-hatch predicates (<c>.Where(pred, ...)</c>
///     and <c>.Must(pred, ...)</c>). This is the v1 predicate input contract (GRAMMAR §5.6); it
///     grows additively as extraction learns new facts. Predicates are stored on the model but never
///     evaluated — the mandatory description is what renders, not the lambda.
/// </summary>
// Read by the predicate authors this contract exists for rather than through the interface
// in-solution, so solution-wide search finds only the implementations and calls the members unused.
// ReSharper disable UnusedMemberInSuper.Global
public interface ITypeInfo
{
    /// <summary>The simple (unqualified) type name.</summary>
    string Name { get; }

    /// <summary>The declaring namespace.</summary>
    string Namespace { get; }

    /// <summary>The kind of type.</summary>
    TypeKind Kind { get; }

    /// <summary>The name of the project (assembly) that declares the type.</summary>
    string ProjectName { get; }

    /// <summary>The attributes applied to the type.</summary>
    IReadOnlyList<ITypeInfo> Attributes { get; }

    /// <summary>The base type, or null for interfaces and <see cref="object" />.</summary>
    ITypeInfo? BaseType { get; }

    /// <summary>The interfaces implemented directly by the type.</summary>
    IReadOnlyList<ITypeInfo> Interfaces { get; }

    /// <summary>The declared accessibility.</summary>
    Accessibility Accessibility { get; }

    /// <summary>
    ///     Whether the type is sealed, in C# declaration semantics: static classes report
    ///     <c>false</c> (their abstract+sealed metadata encoding is normalized away); structs,
    ///     enums, and delegates report <c>true</c> (kind-implied).
    /// </summary>
    bool IsSealed { get; }

    /// <summary>
    ///     Whether the type is a static class. A static type is never <see cref="IsSealed" /> or
    ///     <see cref="IsAbstract" /> here, whatever the raw encoding says.
    /// </summary>
    bool IsStatic { get; }

    /// <summary>
    ///     Whether the type is abstract, in C# declaration semantics: static classes report
    ///     <c>false</c> (their abstract+sealed metadata encoding is normalized away); interfaces
    ///     report <c>true</c> (kind-implied).
    /// </summary>
    bool IsAbstract { get; }

    /// <summary>
    ///     Whether the type is a record (record class or record struct). Records carry no
    ///     <see cref="TypeKind" /> of their own — this flag is the v1 record story (GRAMMAR §5.2).
    /// </summary>
    bool IsRecord { get; }

    /// <summary>
    ///     Whether a generator emitted the type — the fact <c>.Authored()</c> filters on (GRAMMAR §5.2,
    ///     §5.6). True when <c>System.CodeDom.Compiler.GeneratedCodeAttribute</c> sits on the type or on any
    ///     type containing it — so the nested types a generator emits inside an attributed container ride
    ///     along without carrying their own attribute — or when every file declaring it is generator
    ///     output, which is either a source-generated document or a file led by an auto-generated banner
    ///     comment.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <em>Every</em> declaring file, not any: the attribute is a claim about a type and the file
    ///         signals are claims about a file, so a file signal lifts to a type only when it holds of every
    ///         file declaring it. A partial type whose author writes one part and whose generator writes the
    ///         other is therefore authored — someone can act on a violation reported against it.
    ///     </para>
    ///     <para>
    ///         Nothing is inferred from a file path, an <c>obj/</c> directory, or a naming convention: those
    ///         vary per generator and per build, and they answer the question 'may a tool edit this file'
    ///         rather than this one. A hand-written type that carries the attribute is still reported
    ///         generated — the signals are read, never second-guessed.
    ///     </para>
    /// </remarks>
    bool IsGenerated { get; }

    /// <summary>
    ///     The distinct file paths declaring the type (several for a partial type), verbatim as
    ///     compiled, in declaration-site (file, line) order. Empty for external (metadata) types.
    /// </summary>
    IReadOnlyList<string> FilePaths { get; }
}
