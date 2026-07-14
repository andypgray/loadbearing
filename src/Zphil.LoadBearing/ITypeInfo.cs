namespace Zphil.LoadBearing;

/// <summary>
///     The read-only view of a type handed to escape-hatch predicates (<c>.Where(pred, ...)</c>
///     and <c>.Must(pred, ...)</c>). This is the v1 predicate input contract (GRAMMAR §5.6); it
///     grows additively as extraction learns new facts. Predicates are stored on the model but never
///     evaluated in Phase 1 — the mandatory description is what renders, not the lambda.
/// </summary>
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
    ///     The distinct file paths declaring the type (several for a partial type), verbatim as
    ///     compiled, in declaration-site (file, line) order. Empty for external (metadata) types.
    /// </summary>
    IReadOnlyList<string> FilePaths { get; }
}