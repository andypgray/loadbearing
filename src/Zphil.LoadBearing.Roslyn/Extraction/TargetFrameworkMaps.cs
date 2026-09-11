using Microsoft.CodeAnalysis;

namespace Zphil.LoadBearing.Roslyn.Extraction;

/// <summary>
///     The empty per-project target-framework map, held once for every carrier of one.
/// </summary>
/// <remarks>
///     A solution whose projects each target a single framework has no discriminators to record, which is the
///     common case — so the map is empty on most runs, on every carrier at once.
/// </remarks>
internal static class TargetFrameworkMaps
{
    /// <summary>
    ///     No project was loaded for a discriminated framework — what a single-target solution carries, and
    ///     the fallback every carrier takes when none was supplied.
    /// </summary>
    internal static readonly IReadOnlyDictionary<ProjectId, string> None = new Dictionary<ProjectId, string>();
}
