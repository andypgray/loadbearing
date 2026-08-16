using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Zphil.LoadBearing.Roslyn.Diagnostics;
using Zphil.LoadBearing.Roslyn.Solutions;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     One-shot MSBuild solution loading for the enforcement path: create a workspace, open the
///     solution, strip unresolved references, normalize the project names, return. A check/render run is
///     one-shot, so none of the machinery a long-lived workspace server would need (watcher, reload,
///     external-edit reconcile, incremental sync, semaphores, two-phase ready tasks, warmup) exists here.
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
    ///     this process — the "a design-time build ran" pin. The binlog-replay path deliberately
    ///     bypasses this method, so a run that replays a capture leaves this counter unchanged. Internal
    ///     test observable, incremented with <see cref="Interlocked" />; never consulted in production.
    /// </summary>
    internal static long LoadCount => Interlocked.Read(ref _loadCount);

    /// <summary>
    ///     Opens <paramref name="solutionPath" /> through a fresh <see cref="MSBuildWorkspace" /> and
    ///     returns the loaded, stripped solution.
    /// </summary>
    /// <param name="solutionPath">
    ///     Absolute path to the <c>.sln</c>/<c>.slnx</c> to load, or to a <c>.slnf</c> filter over one. A
    ///     filter loads the projects it selects plus their transitive references, and what it leaves out is
    ///     reported as <see cref="LoadedSolution.UncheckedProjects" /> rather than as a failure.
    /// </param>
    /// <param name="diagnosticLog">
    ///     Optional sink for workspace-failure diagnostics. Failures are surfaced but never abort the
    ///     load: MSBuildWorkspace reports partial-load problems as diagnostics, and a partial load
    ///     still yields a usable model. These render; they do not decide anything — whether the model is
    ///     incomplete is <see cref="LoadedSolution.FailedProjects" />'s and
    ///     <see cref="LoadedSolution.RestoreFailedProjects" />'s answer.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    ///     <see cref="MethodImplOptions.NoInlining" /> keeps the JIT from resolving MSBuild/Roslyn
    ///     assemblies before <c>MSBuildLocator</c> registration in non-test hosts (the CLI);
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

        Solution solution = await OpenAsync(workspace, solutionPath, ct);
        (Solution stripped, int _, int _) = solution.StripUnresolvedReferences();
        (Solution normalized, IReadOnlyDictionary<ProjectId, string> targetFrameworks) =
            stripped.NormalizeProjectNames();

        // What the load did and did not produce is read off the loaded structure here, at the boundary, so
        // every consumer downstream is handed the same answer rather than re-deriving one from the
        // diagnostic text. The restore verdict is the same idea one file further out: a project whose restore
        // failed loads completely, so nothing in the loaded structure says so and only its assets file can.
        ProjectLoadReport report = ProjectLoadFailures.Detect(normalized, solutionPath);
        IReadOnlyList<string> restoreFailed = RestoreFailures.Detect(normalized, report.Failed);

        return new LoadedSolution(workspace, normalized, targetFrameworks, report, restoreFailed);
    }

    /// <summary>
    ///     The refusal when a solution filter cannot be read. Pure, and internal so the text pins without a
    ///     filesystem — the same split <see cref="SolutionDiscovery.AmbiguousMessage" /> makes.
    /// </summary>
    /// <param name="filterPath">The <c>.slnf</c> that could not be read.</param>
    internal static string UnreadableFilterMessage(string filterPath)
    {
        return $"Could not read the solution filter '{filterPath}'.\n"
               + "A filter must be well-formed JSON whose 'solution' path resolves to a .sln or .slnx, and "
               + "every project it lists must be a member of that solution.";
    }

    // Roslyn's SolutionFilterReader signals every filter fault by throwing a bare Exception out of
    // OpenSolutionAsync — measured as the same "Failed to load solution filter" for malformed JSON, a
    // missing 'solution' key, a solution path that does not resolve, and a project entry naming a
    // non-member. Unmapped it reaches the user as a stack trace. The gate is the extension, never the
    // message: matching wording is the mistake ProjectLoadFailures exists to undo. The original is kept as
    // the inner exception, so a fault that is genuinely not the filter's is still recoverable from a log.
    private static async Task<Solution> OpenAsync(
        MSBuildWorkspace workspace, string solutionPath, CancellationToken ct)
    {
        if (!SolutionProjectFileParser.IsFilterFormat(solutionPath))
            return await workspace.OpenSolutionAsync(solutionPath, cancellationToken: ct);

        try
        {
            return await workspace.OpenSolutionAsync(solutionPath, cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new UserErrorException(UnreadableFilterMessage(solutionPath), ex);
        }
    }
}
