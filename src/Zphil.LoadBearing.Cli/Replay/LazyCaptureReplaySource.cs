using Zphil.LoadBearing.Cli.Pipeline;
using Zphil.LoadBearing.Roslyn.Replay;

namespace Zphil.LoadBearing.Cli.Replay;

/// <summary>
///     The lazy replay source: replays a persisted, structurally-valid build capture — but only
///     when a runner actually acquires a workspace. A fragment-cache hit never acquires, so it stays
///     replay-free and sub-second and byte-identical to a plain cached run.
/// </summary>
/// <remarks>
///     Replays the capture's binlog copy on the one <see cref="AcquireAsync" /> call the runner makes, and
///     owns the <see cref="ReplayedSolution" /> that produces: the handle it hands back is non-owning
///     (<c>owned: null</c>) because the replay's reader backs the solution's lazy per-document text loaders,
///     so releasing it at handle scope would come too early — <see cref="Dispose" /> releases it at run scope
///     instead. A runtime replay failure of a copy the store validated as usable is not fatal — it raises
///     <see cref="CaptureReplayFailedException" /> so the gate can notice-and-fall-back to a design-time build
///     rather than break the run. No MSBuildWorkspace and no design-time build on the success path (the gate
///     registers MSBuildLocator up front, which is what lets the binlog parser resolve its MSBuild assemblies).
/// </remarks>
internal sealed class LazyCaptureReplaySource(string binlogCopyPath) : ISolutionSource, IDisposable
{
    private ReplayedSolution? _replayed;

    /// <summary>
    ///     Releases the replayed solution — its workspace and binlog reader — once the run that acquired it is
    ///     done. A run that never acquired, and one whose replay failed, materialised nothing to release.
    /// </summary>
    public void Dispose()
    {
        _replayed?.Dispose();
    }

    /// <inheritdoc />
    public Task<SolutionHandle> AcquireAsync(string solutionPath, CancellationToken ct)
    {
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

        _replayed = replayed;
        return Task.FromResult(new SolutionHandle(
            replayed.Solution, solutionPath, replayed.LoadDiagnosticsWith(diagnostics), null,
            targetFrameworks: replayed.TargetFrameworks));
    }
}
