using Microsoft.CodeAnalysis;

namespace Zphil.LoadBearing.Roslyn.Extraction;

/// <summary>
///     The empty per-project target-framework map, held once for every carrier of one.
/// </summary>
/// <remarks>
///     A solution whose projects each target a single framework has no discriminators to record, which is the
///     common case — so the map is empty on most runs, on every carrier at once. Five types carry it
///     (<see cref="LoadedSolution" />, <see cref="WorkspaceSnapshot" />, <see cref="WorkspaceSession" />, the
///     replayed solution and the CLI's solution handle) and each used to restate the type to say "none",
///     which is a declaration of the same nothing five times over rather than a shared reading of it.
/// </remarks>
internal static class TargetFrameworkMaps
{
    /// <summary>
    ///     No project was loaded for a discriminated framework — what a single-target solution carries, and
    ///     the fallback every carrier takes when none was supplied.
    /// </summary>
    internal static readonly IReadOnlyDictionary<ProjectId, string> None = new Dictionary<ProjectId, string>();
}
