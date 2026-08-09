using Zphil.LoadBearing.Cli.SpecLoading;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli.Mcp;

/// <summary>
///     The warm solution source: the host-managed counterpart to
///     <see cref="ColdSolutionSource" />. Each <see cref="AcquireAsync" /> discovers the target solution
///     exactly as the cold path does — <see cref="ModelPipeline.DiscoverSolution" /> first, so a discovery
///     failure surfaces the byte-for-byte same <see cref="Roslyn.UserErrorException" /> a cold run raises —
///     then serves the reconciled snapshot the shared <see cref="WorkspaceSession" /> keeps warm across tool
///     calls.
/// </summary>
/// <remarks>
///     The returned handle owns nothing (<c>owned: null</c>): the session outlives the call and keeps
///     the workspace, so disposing the handle is a no-op and a later reconcile cannot pull the solution out
///     from under an in-flight reader (a Roslyn <see cref="Microsoft.CodeAnalysis.Solution" /> survives its
///     workspace's disposal).
///     The handle also carries the session-scoped incremental codebase producer: a closure
///     over this call's <paramref name="session" /> snapshot and the shared <see cref="SessionFragmentStore" />
///     that reuses clean projects' fragments, re-walks only the dirty ∪ dependent set, and memoizes the merge.
///     The extraction seam consults it instead of re-extracting the whole solution on every tool call.
///     <see cref="LoadSpecModel" /> is the same idea on the other input: the spec's model is cached against
///     its DLL's file stamp, so a server answering a hook on every edit reloads it only when the spec
///     project is rebuilt.
/// </remarks>
internal sealed class WarmSolutionSource(WorkspaceSession session, SessionFragmentStore store, SpecModelCache specs)
    : ISolutionSource
{
    /// <inheritdoc />
    public async Task<SolutionHandle> AcquireAsync(string? solution, string workingDirectory, CancellationToken ct)
    {
        string solutionPath = ModelPipeline.DiscoverSolution(solution, workingDirectory);
        WorkspaceSnapshot snapshot = await session.GetCurrentAsync(solutionPath, ct);
        return new SolutionHandle(
            snapshot.Solution, solutionPath, snapshot.Diagnostics, null,
            (exclude, token) => store.GetCodebaseAsync(snapshot, exclude, token));
    }

    /// <inheritdoc />
    public ArchitectureModel LoadSpecModel(string specDllPath)
    {
        return specs.Load(specDllPath);
    }
}
