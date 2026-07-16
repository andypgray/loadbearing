using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     One-shot MSBuild solution loading for the enforcement path: create a workspace, open the
///     solution, strip unresolved references, return. A check/render run is one-shot, so none of
///     the machinery a long-lived workspace server would need (watcher, reload, external-edit
///     reconcile, incremental sync, semaphores, two-phase ready tasks, warmup) exists here.
/// </summary>
/// <remarks>
///     This is the one-shot <em>primitive</em>. The CLI and the xUnit adapter build directly on it and
///     keep its restored-solution staleness contract: each invocation opens a fresh workspace, reads the
///     solution once, and disposes it, so the loaded snapshot is only ever as current as the moment of the
///     load. The other lifetime — a host-managed, long-lived solution that reconciles against disk before
///     each read — lives in <see cref="WorkspaceSession" />, which owns this primitive rather than
///     replacing it.
/// </remarks>
public static class WorkspaceLoader
{
    private static long _loadCount;

    /// <summary>
    ///     The number of times <see cref="LoadAsync" /> has opened an <see cref="MSBuildWorkspace" /> in
    ///     this process — the "a design-time build ran" pin. The Phase 12 binlog-replay path deliberately
    ///     bypasses this method, so a run that replays a capture leaves this counter unchanged. Internal
    ///     test observable, incremented with <see cref="Interlocked" />; never consulted in production.
    /// </summary>
    internal static long LoadCount => Interlocked.Read(ref _loadCount);

    /// <summary>
    ///     Opens <paramref name="solutionPath" /> through a fresh <see cref="MSBuildWorkspace" /> and
    ///     returns the loaded, stripped solution.
    /// </summary>
    /// <param name="solutionPath">Absolute path to the <c>.sln</c>/<c>.slnx</c> to load.</param>
    /// <param name="diagnosticLog">
    ///     Optional sink for workspace-failure diagnostics. Failures are surfaced but never abort the
    ///     load: MSBuildWorkspace reports partial-load problems as diagnostics, and a partial load
    ///     still yields a usable model.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    ///     <see cref="MethodImplOptions.NoInlining" /> keeps the JIT from resolving MSBuild/Roslyn
    ///     assemblies before <c>MSBuildLocator</c> registration in non-test hosts (the Phase 3 CLI);
    ///     in tests registration happens in a <c>[ModuleInitializer]</c>, so ordering is already safe.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task<LoadedSolution> LoadAsync(
        string solutionPath, Action<string>? diagnosticLog = null, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _loadCount);

        var workspace = MSBuildWorkspace.Create();
        workspace.RegisterWorkspaceFailedHandler(e =>
        {
            if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure) diagnosticLog?.Invoke(e.Diagnostic.Message);
        });

        Solution solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: ct);
        (Solution stripped, int _, int _) = solution.StripUnresolvedReferences();

        return new LoadedSolution(workspace, stripped);
    }
}