namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     A directed constructor-injection edge inside one fragment (GRAMMAR §4.7): source and injected type by
///     FQN, plus the distinct <c>file:line</c> sites of the injected constructor parameters (in
///     <see cref="FragmentSite" /> order, already self-injection-filtered at extraction). The merge unions
///     site-sets across fragments per <c>(source, injected)</c> and rewires the endpoints to the shared node
///     instances.
/// </summary>
/// <remarks>
///     Type-pair identity, no member facts — the injection analog of <see cref="FragmentEdge" /> /
///     <see cref="FragmentConstructorEdge" /> and the pure-data counterpart of
///     <see cref="Zphil.LoadBearing.Codebase.InjectionEdge" />. It holds no Roslyn types, so it is
///     System.Text.Json-serializable by design and rides the persisted extraction cache.
/// </remarks>
internal sealed record FragmentInjectionEdge(
    string SourceFullName,
    string InjectedFullName,
    IReadOnlyList<FragmentSite> Sites);