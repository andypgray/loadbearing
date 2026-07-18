namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     One declared member of a fragment's declared type (GRAMMAR §4.6), captured as pure data: its
///     <see cref="MemberFacts">scalar facts</see> and its declaration sites. The member analog of
///     <see cref="FragmentType" />, held in <see cref="FragmentType.DeclaredMembers" /> and unioned into a
///     <see cref="Zphil.LoadBearing.Codebase.MemberNode" /> at merge.
/// </summary>
/// <remarks>
///     <see cref="DeclarationSites" /> is in <see cref="FragmentSite" /> order (a partial method contributes
///     each part; a field-declaration group each declarator), so the serialized fragment is stable and the
///     merge preserves the order.
/// </remarks>
internal sealed record FragmentMember(
    MemberFacts Facts,
    IReadOnlyList<FragmentSite> DeclarationSites);