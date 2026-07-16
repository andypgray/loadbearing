using Microsoft.CodeAnalysis;

namespace Zphil.LoadBearing.Cli.Replay;

/// <summary>
///     The eager replay source (Phase 12 D1): serves a <see cref="Solution" /> the gate has <em>already</em>
///     replayed from an explicit <c>--binlog</c>, so the ingest sanity-check and capture persistence fire
///     deterministically before the run — even when the fragment cache would hit and never acquire a
///     workspace. Like <see cref="Mcp.WarmSolutionSource" /> it discovers first
///     (<see cref="ModelPipeline.DiscoverSolution" />) for byte-for-byte error-text parity with the cold path,
///     then hands back a non-owning handle (<c>owned: null</c>): the gate owns the underlying
///     <see cref="Roslyn.Replay.ReplayedSolution" /> and disposes it once the run completes. No
///     MSBuildWorkspace and no design-time build — the whole point of the bypass (the gate registers
///     MSBuildLocator up front so the binlog parser can resolve its MSBuild assemblies).
/// </summary>
internal sealed class ReplayedSolutionSource(
    Solution replayedSolution,
    string solutionPath,
    IReadOnlyList<string> diagnostics) : ISolutionSource
{
    /// <inheritdoc />
    public Task<SolutionHandle> AcquireAsync(string? solution, string workingDirectory, CancellationToken ct)
    {
        ModelPipeline.DiscoverSolution(solution, workingDirectory);
        return Task.FromResult(new SolutionHandle(replayedSolution, solutionPath, diagnostics, null));
    }
}