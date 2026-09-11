namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     A type in the extracted model, carrying everything a rule can ask about one type: its name and where
///     it is declared, its shape, its hierarchy, its attributes and its declared members. It implements
///     <see cref="ITypeInfo" />, so the facts a <c>Where</c> predicate in a spec sees and the facts here are
///     one surface.
/// </summary>
/// <remarks>
///     Every type the solution declares is present, and so is every framework or package type the solution
///     merely references; <see cref="IsExternal" /> tells the two apart. Compare types by reference and
///     never by name, because one fully-qualified name can belong to two of them.
/// </remarks>
// Minted shallow (identity only) and filled through the internal setters during model construction, which
// is what tolerates self-referential shapes such as `[Foo] class FooAttribute`, where a type is its own
// attribute. FullName is symbol.OriginalDefinition.ToDisplayString().
public sealed class TypeNode : ITypeInfo
{
    internal TypeNode(
        string fullName, string symbolId, string name, string @namespace, TypeKind kind,
        Accessibility accessibility, bool isSealed, bool isStatic, bool isAbstract, bool isRecord,
        bool isGenerated, string projectName, bool isExternal)
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
        IsGenerated = isGenerated;
        ProjectName = projectName;
        IsExternal = isExternal;
        DeclarationSites = Array.Empty<SourceLocation>();
        FilePaths = Array.Empty<string>();
        AlsoDeclaredBy = Array.Empty<string>();
        Interfaces = Array.Empty<ITypeInfo>();
        Attributes = Array.Empty<ITypeInfo>();
        AllInterfaces = Array.Empty<TypeConstruction>();
        BaseTypeChain = Array.Empty<TypeConstruction>();
        AttributeConstructions = Array.Empty<TypeConstruction>();
        Members = Array.Empty<MemberNode>();
    }

    /// <summary>
    ///     Gets the fully-qualified name: namespace-qualified, without <c>global::</c>, a nested type dotted
    ///     onto its container, an open generic carrying its declared type-parameter names —
    ///     <c>MyApp.Web.IHandler&lt;T&gt;</c>, <c>MyApp.Domain.Order.Line</c>. Always the definition's name; a
    ///     constructed generic is never named here.
    /// </summary>
    /// <remarks>
    ///     The name is not an identity. Where a project declares a name a referenced assembly also supplies, two
    ///     types wear it, told apart by <see cref="ProjectName" /> and <see cref="IsExternal" /> — which is why
    ///     every set and lookup over the types in this model compares by reference.
    /// </remarks>
    public string FullName { get; }

    /// <summary>
    ///     Gets the identity a baseline records for this type: its Roslyn documentation comment ID in the
    ///     <c>T:</c> form — <c>T:MyApp.Web.HomeController</c>, <c>T:MyApp.Web.IHandler`1</c> for an open
    ///     generic, <c>T:MyApp.Domain.Order.Line</c> for a nested type — or <c>unresolved:{FullName}</c> where
    ///     the compiler gives the type no such ID. It survives file moves, renames and reformatting, where a
    ///     <c>file:line</c> does not. Two types sharing a <see cref="FullName" /> share this ID as well, so a
    ///     baseline entry written for either covers the other.
    /// </summary>
    public string SymbolId { get; }

    /// <summary>
    ///     Gets whether no compilation in the solution declares this type — a framework or package type the
    ///     model carries so that a rule can name it. An external type has no declaration sites, file paths,
    ///     hierarchy, attributes or members: those lists are all empty and <see cref="BaseType" /> is null. Its
    ///     shape facts (<see cref="Kind" />, <see cref="Accessibility" /> and the modifier flags) are read from
    ///     the referenced assembly and are accurate.
    /// </summary>
    public bool IsExternal { get; }

    /// <summary>
    ///     Gets where the type is declared, one entry per part of a partial type, ordered by file then line.
    ///     Empty for an external type.
    /// </summary>
    public IReadOnlyList<SourceLocation> DeclarationSites { get; internal set; }

    /// <summary>
    ///     Gets the other projects declaring this same fully-qualified name, ordered ordinal — one source file
    ///     compiled into several projects, by a linked <c>&lt;Compile Include&gt;</c>, by shared source or by a
    ///     polyfill. Empty for an ordinary type.
    /// </summary>
    /// <remarks>
    ///     <see cref="ProjectName" /> is the first declarer and the facts here are that declarer's, but a rule
    ///     naming any project in the list still reaches this type (see <see cref="IsDeclaredBy" />), because
    ///     each of them compiles the type itself rather than referencing the first — so a reference from one
    ///     declarer into its own compiled-in copy counts against that declarer and not against the first.
    /// </remarks>
    public IReadOnlyList<string> AlsoDeclaredBy { get; internal set; }

    /// <summary>
    ///     Gets every interface the type implements, directly or through a base class or interface inheritance,
    ///     with type arguments substituted and ordered ordinal by <see cref="TypeConstruction.FullName" />.
    ///     This is what <c>Implementing</c> and <c>MustImplement</c> read. Empty for an external type, so those
    ///     never match an external type as a subject; a declared type's list runs on through the assemblies it
    ///     references, so an external interface is still matched when a declared type implements it.
    /// </summary>
    public IReadOnlyList<TypeConstruction> AllInterfaces { get; internal set; }

    /// <summary>
    ///     Gets the base types in derivation order, nearest first, ending at <c>System.Object</c>. The order is
    ///     meaningful, so this list is not sorted. This is what <c>DerivedFrom</c> and <c>MustDeriveFrom</c>
    ///     read. Empty for an interface and for an external type; a declared type's chain runs on through the
    ///     assemblies it references, so an external base type is still matched.
    /// </summary>
    public IReadOnlyList<TypeConstruction> BaseTypeChain { get; internal set; }

    /// <summary>
    ///     Gets the attributes written on the type's own declaration, ordered ordinal by
    ///     <see cref="TypeConstruction.FullName" />; attributes inherited from a base type are not included. A
    ///     generic attribute is recorded like any other, keeping both its definition and its construction. This
    ///     is what <c>AttributedWith</c> and <c>MustBeAttributedWith</c> read. Empty for an external type.
    /// </summary>
    public IReadOnlyList<TypeConstruction> AttributeConstructions { get; internal set; }

    /// <summary>
    ///     Gets the type's declared methods, properties, fields and events, ordered ordinal by
    ///     <see cref="MemberNode.SymbolId" /> — the members a member selection such as <c>Types.Methods</c> or
    ///     <c>Types.Members</c> ranges over. Each member's <see cref="MemberNode.DeclaringType" /> is this same
    ///     instance. Constructors, accessors, operators, conversions, finalizers, indexers, explicit interface
    ///     implementations and compiler-generated members are all left out. Empty for an external type, and for
    ///     an enum or a delegate, which contribute no members.
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
    public bool IsGenerated { get; }

    /// <inheritdoc />
    public ITypeInfo? BaseType { get; internal set; }

    /// <inheritdoc />
    public IReadOnlyList<ITypeInfo> Interfaces { get; internal set; }

    /// <inheritdoc />
    public IReadOnlyList<ITypeInfo> Attributes { get; internal set; }

    /// <summary>
    ///     Reports whether <paramref name="projectName" /> declares this type: <see cref="ProjectName" /> or any
    ///     entry of <see cref="AlsoDeclaredBy" />, compared ordinal. This is the membership test behind a rule
    ///     that names a project — one source file compiled into several projects is one type here, and every
    ///     project that compiles it names it.
    /// </summary>
    // Walked rather than cached, because AlsoDeclaredBy is stamped during model construction, after the node
    // is minted — a roster built at mint time would be stale for every conflated type. The list is empty for
    // every ordinary type, so the walk is one comparison.
    public bool IsDeclaredBy(string projectName)
    {
        if (string.Equals(ProjectName, projectName, StringComparison.Ordinal)) return true;

        IReadOnlyList<string> alsoDeclaredBy = AlsoDeclaredBy;
        for (var i = 0; i < alsoDeclaredBy.Count; i++)
            if (string.Equals(alsoDeclaredBy[i], projectName, StringComparison.Ordinal))
                return true;

        return false;
    }
}
