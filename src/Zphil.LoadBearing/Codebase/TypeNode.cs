namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     A type in the extracted codebase model — the enforcement substrate's node. It implements
///     <see cref="ITypeInfo" /> so the escape-hatch predicate contract and this Roslyn-derived data
///     are the same surface; the checker can therefore be pure Core logic over these nodes.
/// </summary>
/// <remarks>
///     Nodes are minted shallow (identity only) and filled via internal setters during model
///     construction — this tolerates self-referential shapes such as <c>[Foo] class FooAttribute</c>,
///     where a type is its own attribute. <see cref="FullName" /> is
///     <c>symbol.OriginalDefinition.ToDisplayString()</c>: namespace-qualified, no <c>global::</c>,
///     nested types dotted, open generics carrying their declared type-parameter names
///     (e.g. <c>MyApp.Web.IHandler&lt;T&gt;</c>, <c>MyApp.Domain.Order.Line</c>).
/// </remarks>
public sealed class TypeNode : ITypeInfo
{
    internal TypeNode(
        string fullName, string symbolId, string name, string @namespace, TypeKind kind,
        Accessibility accessibility, bool isSealed, bool isStatic, bool isAbstract, bool isRecord,
        string projectName, bool isExternal)
    {
        FullName = fullName;
        SymbolId = symbolId;
        Name = name;
        Namespace = @namespace;
        Kind = kind;
        Accessibility = accessibility;
        IsSealed = isSealed;
        IsStatic = isStatic;
        IsAbstract = isAbstract;
        IsRecord = isRecord;
        ProjectName = projectName;
        IsExternal = isExternal;
        DeclarationSites = Array.Empty<SourceLocation>();
        FilePaths = Array.Empty<string>();
        Interfaces = Array.Empty<ITypeInfo>();
        Attributes = Array.Empty<ITypeInfo>();
        AllInterfaces = Array.Empty<TypeConstruction>();
        BaseTypeChain = Array.Empty<TypeConstruction>();
        AttributeConstructions = Array.Empty<TypeConstruction>();
        Members = Array.Empty<MemberNode>();
    }

    /// <summary>The fully-qualified name; the model's identity key. See remarks for the exact form.</summary>
    public string FullName { get; }

    /// <summary>
    ///     The stable symbol ID a baseline keys on (GRAMMAR §4.3): the Roslyn
    ///     <c>DocumentationCommentId</c> of the original definition (the <c>T:</c> form —
    ///     <c>T:MyApp.Web.HomeController</c>, <c>T:MyApp.Web.IHandler`1</c> for an open generic,
    ///     <c>T:MyApp.Domain.Order.Line</c> for a nested type), or an <c>unresolved:{FullName}</c>
    ///     fallback when the symbol has no DocID. Unlike a <c>file:line</c> site, it survives file
    ///     moves, renames, and formatting.
    /// </summary>
    public string SymbolId { get; }

    /// <summary>
    ///     True when no input compilation declares this type — a shallow node standing in for a
    ///     BCL/NuGet type. External nodes carry empty <see cref="DeclarationSites" />,
    ///     <see cref="FilePaths" />, <see cref="Interfaces" />, and <see cref="Attributes" />, and a
    ///     null <see cref="BaseType" /> — but their scalar shape facts (<see cref="Kind" />,
    ///     <see cref="Accessibility" />, the modifier flags) are accurate, read from the metadata
    ///     symbol at mint time.
    /// </summary>
    public bool IsExternal { get; }

    /// <summary>
    ///     The declaration sites — one per part for a partial type, ordered by (file, line). Empty
    ///     for external types.
    /// </summary>
    public IReadOnlyList<SourceLocation> DeclarationSites { get; internal set; }

    /// <summary>
    ///     The full transitive, substituted interface closure (<c>symbol.AllInterfaces</c>
    ///     semantics) — interfaces reached through base classes and interface inheritance, with type
    ///     arguments substituted. Ordered ordinal by <see cref="TypeConstruction.FullName" />. Empty
    ///     for external types (hierarchy adjectives never match external targets — a documented
    ///     boundary). Backs <c>Implementing</c> / <c>MustImplement</c> in the checker (GRAMMAR §5.2).
    /// </summary>
    public IReadOnlyList<TypeConstruction> AllInterfaces { get; internal set; }

    /// <summary>
    ///     The transitive base-type chain in <em>nearest-first</em> derivation order (the order is
    ///     meaningful, so it is not sorted), terminating at <c>System.Object</c>. Empty for
    ///     interfaces and external types. Backs <c>DerivedFrom</c> / <c>MustDeriveFrom</c>.
    /// </summary>
    public IReadOnlyList<TypeConstruction> BaseTypeChain { get; internal set; }

    /// <summary>
    ///     The declared attributes as constructions (<c>GetAttributes()</c>; no inheritance), ordered
    ///     ordinal by <see cref="TypeConstruction.FullName" /> — C# 11 generic attributes flow through
    ///     uniformly. Empty for external types. Backs <c>AttributedWith</c> / <c>MustBeAttributedWith</c>.
    /// </summary>
    public IReadOnlyList<TypeConstruction> AttributeConstructions { get; internal set; }

    /// <summary>
    ///     The type's declared members (GRAMMAR §4.6) — its inventoried methods, properties, fields, and
    ///     events (accessors/constructors/operators/finalizers/indexers and compiler-generated members
    ///     excluded), ordered ordinal by <see cref="MemberNode.SymbolId" />. Each member's
    ///     <see cref="MemberNode.DeclaringType" /> is <em>this</em> node. Empty for external types and for
    ///     enum/delegate types (which contribute no inventory). Backs the member selections' subject set.
    /// </summary>
    public IReadOnlyList<MemberNode> Members { get; internal set; }

    /// <inheritdoc />
    public IReadOnlyList<string> FilePaths { get; internal set; }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Namespace { get; }

    /// <inheritdoc />
    public TypeKind Kind { get; }

    /// <inheritdoc />
    public string ProjectName { get; }

    /// <inheritdoc />
    public Accessibility Accessibility { get; }

    /// <inheritdoc />
    public bool IsSealed { get; }

    /// <inheritdoc />
    public bool IsStatic { get; }

    /// <inheritdoc />
    public bool IsAbstract { get; }

    /// <inheritdoc />
    public bool IsRecord { get; }

    /// <inheritdoc />
    public ITypeInfo? BaseType { get; internal set; }

    /// <inheritdoc />
    public IReadOnlyList<ITypeInfo> Interfaces { get; internal set; }

    /// <inheritdoc />
    public IReadOnlyList<ITypeInfo> Attributes { get; internal set; }
}