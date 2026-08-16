namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The seam between a workspace command and the host that serves it. <see cref="AcquireAsync" />
///     hands back a <see cref="SolutionHandle" /> — the loaded solution
///     (<see cref="SolutionHandle.Solution" />) plus the solution path and the workspace-load
///     diagnostics — and <see cref="LoadSpecModel" /> produces the model behind a spec DLL.
/// </summary>
/// <remarks>
///     Callers <c>using</c> the handle whatever the source: an implementation that opens its own workspace
///     releases it on disposal, and one serving a snapshot it does not own no-ops.
/// </remarks>
internal interface ISolutionSource
{
    /// <summary>
    ///     Returns the already-discovered solution at <paramref name="solutionPath" />, loaded. Load
    ///     failures surface exactly as the cold path raises them, preserving per-call error-text parity.
    /// </summary>
    /// <remarks>
    ///     Discovery is the caller's — <see cref="CodebaseSource" /> performs it once, before any source is
    ///     asked for anything, so a discovery failure is raised at the one place that can raise it and a warm
    ///     host does not repeat the ancestor walk on every tool call.
    /// </remarks>
    /// <param name="solutionPath">The discovered, absolute solution file to serve.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<SolutionHandle> AcquireAsync(string solutionPath, CancellationToken ct);

    /// <summary>
    ///     The validated model behind <paramref name="specDllPath" />. The default is a full one-shot load —
    ///     what a one-shot process wants — and a host whose lifetime spans many runs overrides it to cache
    ///     the model against the DLL's file stamp, since a spec changes only when its project is rebuilt.
    ///     Load failures surface identically either way.
    /// </summary>
    /// <param name="specDllPath">The resolved spec DLL to load.</param>
    ArchitectureModel LoadSpecModel(string specDllPath)
    {
        return ModelPipeline.LoadModel(specDllPath);
    }
}
