namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     A directed reference edge inside one fragment: source and target by FQN, plus the distinct
///     <c>file:line</c> sites where the reference occurs (in <see cref="FragmentSite" /> order, already
///     self-edge-filtered at extraction). The merge unions site-sets across fragments per
///     <c>(source, target)</c> and rewires the endpoints to the shared node instances.
/// </summary>
internal sealed record FragmentEdge(
    string SourceFullName,
    string TargetFullName,
    IReadOnlyList<FragmentSite> Sites);