namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     A directed throw edge inside one fragment (GRAMMAR §4.8): source and thrown type by FQN, plus the
///     distinct <c>file:line</c> sites of the <c>throw</c> statements and throw expressions (in
///     <see cref="FragmentSite" /> order, already self-throw-filtered at extraction). The thrown type is the
///     thrown expression's static type; a bare rethrow (<c>throw;</c>) records nothing. The merge unions
///     site-sets across fragments per <c>(source, thrown)</c> and rewires the endpoints to the shared node
///     instances.
/// </summary>
/// <remarks>
///     Type-pair identity, no member facts — the throw analog of <see cref="FragmentEdge" /> /
///     <see cref="FragmentInjectionEdge" /> and the pure-data counterpart of
///     <see cref="Zphil.LoadBearing.Codebase.ThrowEdge" />. It holds no Roslyn types, so it is
///     System.Text.Json-serializable by design and rides the persisted extraction cache.
/// </remarks>
internal sealed record FragmentThrowEdge(
    string SourceFullName,
    string ThrownFullName,
    IReadOnlyList<FragmentSite> Sites);