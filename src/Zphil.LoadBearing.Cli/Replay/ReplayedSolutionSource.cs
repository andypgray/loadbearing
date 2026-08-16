using Microsoft.CodeAnalysis;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli.Replay;

/// <summary>
///     The eager replay source: serves a <see cref="Solution" /> the gate has <em>already</em>
///     replayed from an explicit <c>--binlog</c>, so the ingest sanity-check and capture persistence fire
///     deterministically before the run — even when the fragment cache would hit and never acquire a
///     workspace.
/// </summary>
/// <remarks>
///     Hands back a non-owning handle (<c>owned: null</c>): the gate owns the underlying
///     <see cref="Roslyn.Replay.ReplayedSolution" /> and disposes it once the run completes. No
///     MSBuildWorkspace and no design-time build — the whole point of the bypass (the gate registers
///     MSBuildLocator up front so the binlog parser can resolve its MSBuild assemblies).
/// </remarks>
internal sealed class ReplayedSolutionSource(
    Solution replayedSolution,
    WorkspaceDiagnostics loadDiagnostics,
    IReadOnlyDictionary<ProjectId, string>? targetFrameworks = null) : ISolutionSource
{
    /// <inheritdoc />
    public Task<SolutionHandle> AcquireAsync(string solutionPath, CancellationToken ct)
    {
        return Task.FromResult(new SolutionHandle(
            replayedSolution, solutionPath, loadDiagnostics, null, targetFrameworks: targetFrameworks));
    }
}
