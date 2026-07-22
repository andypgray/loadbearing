namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     A directed exposure edge inside one fragment (GRAMMAR §4.9): source and exposed type by FQN, plus the
///     distinct <c>file:line</c> sites of the exposing members (in <see cref="FragmentSite" /> order, already
///     self-exposure-filtered at extraction). An exposure edge is minted from every public signature position
///     (return, parameter, property/field/event type) of an effectively-public member. The merge unions
///     site-sets across fragments per <c>(source, exposed)</c> and rewires the endpoints to the shared node
///     instances.
/// </summary>
/// <remarks>
///     Type-pair identity, no member facts — the exposure analog of <see cref="FragmentEdge" /> /
///     <see cref="FragmentInjectionEdge" /> and the pure-data counterpart of
///     <see cref="Zphil.LoadBearing.Codebase.ExposureEdge" />. It holds no Roslyn types, so it is
///     System.Text.Json-serializable by design and rides the persisted extraction cache.
/// </remarks>
internal sealed record FragmentExposureEdge(
    string SourceFullName,
    string ExposedFullName,
    IReadOnlyList<FragmentSite> Sites);