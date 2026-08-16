using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     One declared member of a fragment's declared type (GRAMMAR §4.6), captured as pure data: its
///     <see cref="MemberFacts">scalar facts</see> and its declaration sites. The member analog of
///     <see cref="FragmentType" />, held in <see cref="FragmentType.DeclaredMembers" /> and minted into a
///     <see cref="Zphil.LoadBearing.Codebase.MemberNode" /> by <see cref="ToMemberNode" />.
/// </summary>
/// <remarks>
///     <see cref="DeclarationSites" /> is in <see cref="FragmentSite" /> order (a partial method contributes
///     each part; a field-declaration group each declarator), so the serialized fragment is stable and the
///     merge preserves the order.
/// </remarks>
internal sealed record FragmentMember(
    MemberFacts Facts,
    IReadOnlyList<FragmentSite> DeclarationSites)
{
    /// <summary>
    ///     Mints the merged model's node for this member (GRAMMAR §4.6) — the projection the merge applies
    ///     to the winning fragment's member inventory, taken whole.
    /// </summary>
    /// <remarks>
    ///     The two site projections are the merge's type-side ones, taken from <see cref="FragmentSiteSets" />
    ///     rather than restated here — including the §5.6 first-occurrence file-order contract, which is now
    ///     stated once where both ends of the pipeline read it. The parameter facts are already in
    ///     declaration order and the attribute facts ordinal by constructed name, so each Select preserves
    ///     order and no merge path duplicates or reorders them. Unlike the type-side attribute list, the
    ///     member's stays string-side: no external node is minted for an attribute only a member wears.
    /// </remarks>
    public MemberNode ToMemberNode(TypeNode declaringType)
    {
        IReadOnlyList<SourceLocation> declarationSites = FragmentSiteSets.Locations(DeclarationSites);
        IReadOnlyList<string> filePaths = FragmentSiteSets.FilePaths(DeclarationSites);
        List<ParameterNode> parameters = Facts.Parameters.Select(p => new ParameterNode(p.Name, p.TypeFullName)).ToList();
        List<AttributeNode> attributes = Facts.Attributes.Select(a => new AttributeNode(a.DefinitionFullName, a.ConstructedName)).ToList();

        return new MemberNode(
            declaringType,
            symbolId: Facts.SymbolId,
            name: Facts.Name,
            kind: Facts.Kind,
            accessibility: Facts.Accessibility,
            isStatic: Facts.IsStatic,
            isAbstract: Facts.IsAbstract,
            isVirtual: Facts.IsVirtual,
            isAsync: Facts.IsAsync,
            returnTypeFullName: Facts.ReturnTypeFullName,
            memberTypeFullName: Facts.MemberTypeFullName,
            declarationSites,
            filePaths,
            parameters,
            attributes);
    }
}
