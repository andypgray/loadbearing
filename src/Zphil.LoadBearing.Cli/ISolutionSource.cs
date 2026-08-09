namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The seam between a workspace command and the host that serves it. <see cref="AcquireAsync" />
///     discovers and hands back a <see cref="SolutionHandle" /> — the loaded solution
///     (<see cref="SolutionHandle.Solution" />) plus the discovered path and the workspace-load
///     diagnostics — and <see cref="LoadSpecModel" /> produces the model behind a spec DLL.
/// </summary>
/// <remarks>
///     Callers <c>using</c> the handle whatever the source: an implementation that opens its own workspace
///     releases it on disposal, and one serving a snapshot it does not own no-ops.
/// </remarks>
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
