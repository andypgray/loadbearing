namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     A directed construction edge inside one fragment (GRAMMAR §4.5): source and constructed type by FQN,
///     plus the distinct <c>file:line</c> sites where the object creation occurs (in <see cref="FragmentSite" />
///     order, already self-construction-filtered at extraction). The merge unions site-sets across fragments
///     per <c>(source, constructed)</c> and rewires the endpoints to the shared node instances.
/// </summary>
/// <remarks>
///     Type-pair identity, no member facts — the construction analog of <see cref="FragmentEdge" /> and the
///     pure-data counterpart of <see cref="Zphil.LoadBearing.Codebase.ConstructorEdge" />. It holds no Roslyn
///     types, so it is System.Text.Json-serializable by design and rides the persisted extraction cache.
/// </remarks>
internal sealed record FragmentConstructorEdge(
    string SourceFullName,
    string ConstructedFullName,
    IReadOnlyList<FragmentSite> Sites);