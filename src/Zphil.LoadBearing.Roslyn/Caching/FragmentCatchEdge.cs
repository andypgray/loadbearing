namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     A directed catch edge inside one fragment (GRAMMAR §4.8): source and caught type by FQN, plus the
///     distinct <c>file:line</c> sites of the <c>catch</c> clauses (in <see cref="FragmentSite" /> order,
///     already self-catch-filtered at extraction). A bare <c>catch</c> records <c>System.Exception</c> as the
///     caught type. The merge unions site-sets across fragments per <c>(source, caught)</c> and rewires the
///     endpoints to the shared node instances.
/// </summary>
/// <remarks>
///     Type-pair identity, no member facts — the catch analog of <see cref="FragmentEdge" /> /
///     <see cref="FragmentConstructorEdge" /> and the pure-data counterpart of
///     <see cref="Zphil.LoadBearing.Codebase.CatchEdge" />. It holds no Roslyn types, so it is
///     System.Text.Json-serializable by design and rides the persisted extraction cache.
/// </remarks>
internal sealed record FragmentCatchEdge(
    string SourceFullName,
    string CaughtFullName,
    IReadOnlyList<FragmentSite> Sites);