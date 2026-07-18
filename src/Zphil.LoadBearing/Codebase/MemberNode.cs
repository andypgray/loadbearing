namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     A declared member of a solution-declared type in the extracted model (GRAMMAR §4.6) — the
///     member-subject substrate's node, the inventory a member selection ranges over. It implements
///     <see cref="IMemberInfo" /> so the member escape-hatch predicate contract and this Roslyn-derived
///     data are one surface, exactly as <see cref="TypeNode" /> implements <see cref="ITypeInfo" />.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="DeclaringType" /> is the <em>same</em> <see cref="TypeNode" /> instance held by
///         <see cref="CodebaseModel.Types" /> and by <see cref="TypeNode.Members" /> (reference equality,
///         not just name equality) — a member and its declaring type are one object graph, so a predicate
///         reaching <c>m.DeclaringType</c> sees the identical facts the type-side surface exposes.
///     </para>
///     <para>
///         The flags carry C# declaration semantics, not IL (GRAMMAR §4.6): an <c>override</c> member is
///         not <see cref="IsVirtual" />, an interface member is <see cref="IsAbstract" />. Exactly one of
///         <see cref="ReturnTypeFullName" /> (methods; <c>System.Void</c> for void) and
///         <see cref="MemberTypeFullName" /> (properties/fields/events) is non-null. Only solution-declared
///         types carry members; external nodes hold an empty <see cref="TypeNode.Members" />.
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
        IReadOnlyList<string> filePaths)
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
    }

    /// <summary>
    ///     The stable symbol ID a baseline keys on (GRAMMAR §4.3): the Roslyn
    ///     <c>DocumentationCommentId</c> of the member's original definition — <c>M:</c> for a method,
    ///     <c>P:</c> for a property, <c>F:</c> for a field, <c>E:</c> for an event — or an
    ///     <c>unresolved:{declaring FQN}.{name}</c> fallback when the symbol has no DocID. Unlike a
    ///     <c>file:line</c> site it survives file moves, renames, and formatting.
    /// </summary>
    public string SymbolId { get; }

    /// <summary>
    ///     The declaration sites — one per part for a partial member (and each declarator for a field
    ///     group), ordered by (file, line). The <c>file:line</c> a member-shape violation cites.
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
    public IReadOnlyList<string> FilePaths { get; }
}