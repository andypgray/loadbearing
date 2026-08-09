namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The seam between a workspace command and the host that serves it. <see cref="AcquireAsync" />
///     discovers and hands back a <see cref="SolutionHandle" /> — the loaded solution
///     (<see cref="SolutionHandle.Solution" />) plus the discovered path and the workspace-load
///     diagnostics — and <see cref="LoadSpecModel" /> produces the model behind a spec DLL. Two
///     implementations share this contract: <see cref="ColdSolutionSource" /> opens a fresh one-shot
///     workspace per call and loads a spec once per process (the CLI/adapter lifetime), while the warm MCP
///     source serves a reconciled snapshot cached across tool calls, caches the spec model beside it, and
///     owns nothing the caller must dispose. Callers <c>using</c> the handle either way; disposal releases
///     the cold workspace and no-ops on the warm snapshot.
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

    /// <summary>
    ///     The validated model behind <paramref name="specDllPath" />. The default is a full one-shot load —
    ///     what a one-shot process wants and what every source but the warm MCP one uses — and a host whose
    ///     lifetime spans many runs overrides it to cache the model against the DLL's file stamp, since a
    ///     spec changes only when its project is rebuilt. Load failures surface identically either way.
    /// </summary>
    /// <param name="specDllPath">The resolved spec DLL to load.</param>
    ArchitectureModel LoadSpecModel(string specDllPath)
    {
        return ModelPipeline.LoadModel(specDllPath);
    }
}
