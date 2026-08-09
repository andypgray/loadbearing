using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     The scalar shape facts of one type, extracted from a Roslyn symbol but holding no Roslyn types —
///     the pure-data payload shared by a fragment's declared types and its externals, and the payload
///     <see cref="ToTypeNode" /> mints a <see cref="Zphil.LoadBearing.Codebase.TypeNode" /> from.
/// </summary>
/// <remarks>
///     These are read once per input from the symbol's <c>OriginalDefinition</c> so a persisted fragment
///     can rebuild the node without re-binding. <see cref="Kind" /> and <see cref="Accessibility" /> are
///     the Core enums (never Roslyn's), so this record — like the rest of the fragment DTO — carries no
///     dependency on <c>Microsoft.CodeAnalysis</c> and round-trips through System.Text.Json unchanged.
/// </remarks>
internal sealed record TypeFacts(
    string FullName,
    string SymbolId,
    string Name,
    string Namespace,
    TypeKind Kind,
    Accessibility Accessibility,
    bool IsSealed,
    bool IsStatic,
    bool IsAbstract,
    bool IsRecord,
    bool IsGenerated)
{
    /// <summary>
    ///     Mints the shallow node these facts describe; the merge populates hierarchy, sites, and members
    ///     afterwards.
    /// </summary>
    /// <remarks>
    ///     External nodes (<paramref name="isExternal" /> true) stand in for types no input compilation
    ///     declares and stay shallow for good.
    /// </remarks>
    public TypeNode ToTypeNode(string projectName, bool isExternal)
    {
        return new TypeNode(
            fullName: FullName,
            symbolId: SymbolId,
            name: Name,
            @namespace: Namespace,
            kind: Kind,
            accessibility: Accessibility,
            isSealed: IsSealed,
            isStatic: IsStatic,
            isAbstract: IsAbstract,
            isRecord: IsRecord,
            isGenerated: IsGenerated,
            projectName,
            isExternal);
    }
}
