using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     A directed member-use edge inside one fragment (GRAMMAR §4.5): the source type by FQN, the used
///     member's declaring type by FQN, its simple name and <see cref="MemberKind" />, its stable
///     <see cref="MemberSymbolId">DocumentationCommentId</see>, and the distinct <c>file:line</c> sites
///     where the use occurs (in <see cref="FragmentSite" /> order, already same-type-filtered at
///     extraction). The merge unions site-sets across fragments per (source, member SymbolId) and rewires
///     the endpoints to the shared node instances.
/// </summary>
/// <remarks>
///     <see cref="TargetContainingTypeFullName" />, <see cref="MemberName" />, and
///     <see cref="MemberKind" /> are all functions of the member symbol (never re-parsed from the DocID)
///     and are carried alongside <see cref="MemberSymbolId" /> so the merge can build the model node
///     without re-binding — the member analog of <see cref="FragmentEdge" /> plus the member's own facts.
///     The pure-data counterpart of <see cref="Zphil.LoadBearing.Codebase.MemberEdge" />.
/// </remarks>
internal sealed record FragmentMemberEdge(
    string SourceFullName,
    string TargetContainingTypeFullName,
    string MemberName,
    string MemberSymbolId,
    MemberKind MemberKind,
    IReadOnlyList<FragmentSite> Sites);