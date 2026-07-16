namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The seam between a workspace command and the solution it reads. <see cref="AcquireAsync" />
///     discovers and hands back a <see cref="SolutionHandle" /> — the loaded solution
///     (<see cref="SolutionHandle.Solution" />) plus the discovered path and the workspace-load
///     diagnostics. Two implementations share this contract: <see cref="ColdSolutionSource" /> opens a
///     fresh one-shot workspace per call (the CLI/adapter lifetime), while the warm MCP source
///     (Phase 11 D1) serves a reconciled snapshot cached across tool calls and owns nothing the caller
///     must dispose. Callers <c>using</c> the handle either way; disposal releases the cold workspace and
///     no-ops on the warm snapshot.
/// </summary>
internal interface ISolutionSource
{
    /// <summary>
    ///     Discovers the target solution (an explicit file, a directory to search, or a walk-up from
    ///     <paramref name="workingDirectory" />) and returns it loaded. Discovery and load failures
    ///     surface exactly as the cold path raises them, preserving per-call error-text parity.
    /// </summary>
    /// <param name="solution">Explicit solution file, a directory to search, or <c>null</c> to walk up.</param>
    /// <param name="workingDirectory">The directory a bare (<c>null</c> <paramref name="solution" />) discovery walks up from.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<SolutionHandle> AcquireAsync(string? solution, string workingDirectory, CancellationToken ct);
}