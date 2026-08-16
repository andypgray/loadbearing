using Zphil.LoadBearing.Roslyn.Replay;

namespace Zphil.LoadBearing.Cli.Replay;

/// <summary>
///     The lazy replay source: replays a persisted, structurally-valid build capture — but only
///     when a runner actually acquires a workspace. A fragment-cache hit never acquires, so it stays
///     replay-free and sub-second and byte-identical to a plain cached run.
/// </summary>
/// <remarks>
///     Discovers first for error-text parity, then replays the capture's binlog copy on the one
///     <see cref="AcquireAsync" /> call the runner makes.
///     The produced <see cref="ReplayedSolution" /> is exposed on <see cref="Replayed" /> for the gate
///     to dispose (the handle itself is non-owning, <c>owned: null</c>); a runtime replay failure of a copy
///     the store validated as usable is not fatal — it raises <see cref="CaptureReplayFailedException" /> so
///     the gate can notice-and-fall-back to a design-time build rather than break the run. No MSBuildWorkspace
///     and no design-time build on the success path (the gate registers MSBuildLocator up front, which is what
///     lets the binlog parser resolve its MSBuild assemblies).
/// </remarks>
internal sealed class LazyCaptureReplaySource(string binlogCopyPath) : ISolutionSource
{
    /// <summary>The replayed solution once <see cref="AcquireAsync" /> has run, else null. The gate disposes it.</summary>
    public ReplayedSolution? Replayed { get; private set; }

    /// <inheritdoc />
    public Task<SolutionHandle> AcquireAsync(string? solution, string workingDirectory, CancellationToken ct)
    {
        string solutionPath = ModelPipeline.DiscoverSolution(solution, workingDirectory);

        var diagnostics = new List<string>();
        ReplayedSolution replayed;
        try
        {
            replayed = BinlogReplayer.Replay(binlogCopyPath, diagnostics.Add, ct);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is the caller's, not a bad capture: let it propagate rather than be reported as a
            // replay failure that would send the gate off to re-run the whole thing cold.
            throw;
        }
        catch (Exception ex)
        {
            throw new CaptureReplayFailedException(BinlogReplayMessages.CaptureReplayFailedNotice(ex.Message), ex);
        }

        Replayed = replayed;
        return Task.FromResult(new SolutionHandle(
            replayed.Solution, solutionPath, diagnostics, null, targetFrameworks: replayed.TargetFrameworks,
            failedProjects: replayed.FailedProjects));
    }
}
