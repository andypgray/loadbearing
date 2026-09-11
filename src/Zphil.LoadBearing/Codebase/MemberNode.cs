namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     A member the solution declares: one method, property, field or event of a <see cref="TypeNode" />,
///     and the unit a member selection such as <c>Types.Methods</c> or <c>Types.Members</c> ranges over. It
///     implements <see cref="IMemberInfo" />, so the facts a <c>Where</c> predicate in a spec sees and the
///     facts here are one surface.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="DeclaringType" /> is the very <see cref="TypeNode" /> instance held by
///         <see cref="CodebaseModel.Types" /> and by <see cref="TypeNode.Members" />, so a predicate reaching
///         through it sees the identical type facts. Only the types the solution declares carry members: a
///         referenced framework or package type has none, and neither has an enum or a delegate.
///     </para>
///     <para>
///         The flags describe the declaration as C# spells it rather than the IL behind it. An
///         <c>override</c> member is not <see cref="IsVirtual" />, every interface member is
///         <see cref="IsAbstract" />, and a <c>const</c> field is not <see cref="IsReadOnly" /> — those two
///         are separate declarations, where <see cref="HasInitOnlySetter" /> instead narrows
///         <see cref="HasSetter" />. Exactly one of <see cref="ReturnTypeFullName" /> (methods, with
///         <c>System.Void</c> for a void one) and <see cref="MemberTypeFullName" /> (properties, fields and
///         events) is non-null.
///     </para>
/// </remarks>
public sealed class MemberNode : IMemberInfo
{
    internal MemberNode(
        TypeNode declaringType,
        string symbolId,
        string name,
        MemberKind kind,
        Accessibility accessibility,
        bool isStatic,
        bool isAbstract,
        bool isVirtual,
        bool isAsync,
        string? returnTypeFullName,
        string? memberTypeFullName,
        IReadOnlyList<SourceLocation> declarationSites,
        IReadOnlyList<string> filePaths,
        IReadOnlyList<IParameterInfo>? parameters = null,
        IReadOnlyList<IAttributeInfo>? attributes = null,
        bool hasSetter = false,
        bool hasInitOnlySetter = false,
        bool isReadOnly = false,
        bool isConst = false)
    {
        DeclaringType = declaringType;
        SymbolId = symbolId;
        Name = name;
        Kind = kind;
        Accessibility = accessibility;
        IsStatic = isStatic;
        IsAbstract = isAbstract;
        IsVirtual = isVirtual;
        IsAsync = isAsync;
        ReturnTypeFullName = returnTypeFullName;
        MemberTypeFullName = memberTypeFullName;
        DeclarationSites = declarationSites;
        FilePaths = filePaths;
        Parameters = parameters ?? Array.Empty<IParameterInfo>();
        Attributes = attributes ?? Array.Empty<IAttributeInfo>();
        HasSetter = hasSetter;
        HasInitOnlySetter = hasInitOnlySetter;
        IsReadOnly = isReadOnly;
        IsConst = isConst;
    }

    /// <summary>
    ///     Gets the identity a baseline records for this member: its Roslyn documentation comment ID —
    ///     <c>M:</c> for a method, <c>P:</c> for a property, <c>F:</c> for a field, <c>E:</c> for an event — or
    ///     <c>unresolved:{declaring type}.{name}</c> where the compiler gives the member no such ID. It survives
    ///     file moves, renames and reformatting, where a <c>file:line</c> does not.
    /// </summary>
    public string SymbolId { get; }

    /// <summary>
    ///     Gets where the member is declared, ordered by file then line: one entry per part of a partial member,
    ///     and one per declarator of a field group. A violation about the member's shape or name cites one of
    ///     these.
    /// </summary>
    public IReadOnlyList<SourceLocation> DeclarationSites { get; }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public MemberKind Kind { get; }

    /// <inheritdoc />
    public ITypeInfo DeclaringType { get; }

    /// <inheritdoc />
    public Accessibility Accessibility { get; }

    /// <inheritdoc />
    public bool IsStatic { get; }

    /// <inheritdoc />
    public bool IsAbstract { get; }

    /// <inheritdoc />
    public bool IsVirtual { get; }

    /// <inheritdoc />
    public bool IsAsync { get; }

    /// <inheritdoc />
    public string? ReturnTypeFullName { get; }

    /// <inheritdoc />
    public string? MemberTypeFullName { get; }

    /// <inheritdoc />
    public IReadOnlyList<IParameterInfo> Parameters { get; }

    /// <inheritdoc />
    public IReadOnlyList<IAttributeInfo> Attributes { get; }

    /// <inheritdoc />
    public bool HasSetter { get; }

    /// <inheritdoc />
    public bool HasInitOnlySetter { get; }

    /// <inheritdoc />
    public bool IsReadOnly { get; }

    /// <inheritdoc />
    public bool IsConst { get; }

    /// <inheritdoc />
    public IReadOnlyList<string> FilePaths { get; }

    /// <summary>
    ///     The owning type's <see cref="TypeNode.FullName" />, read off the documented invariant that
    ///     <see cref="DeclaringType" /> is the <see cref="TypeNode" /> that declares this member.
    /// </summary>
    internal string DeclaringTypeFullName => ((TypeNode)DeclaringType).FullName;
}
