using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The one-shot solution source: today's discover-then-load path
///     (<see cref="ModelPipeline.DiscoverSolution" /> → <see cref="WorkspaceLoader.LoadAsync" />) behind the
///     <see cref="ISolutionSource" /> seam. Every <see cref="AcquireAsync" /> opens a fresh
///     <see cref="LoadedSolution" /> and hands it to the returned handle to own, so a <c>using</c> in the
///     caller bounds the workspace to the call — the enforcement path's lifetime, unchanged. Stateless; the
///     default source every runner falls back to when none is injected.
/// </summary>
internal sealed class ColdSolutionSource : ISolutionSource
{
    /// <inheritdoc />
    public async Task<SolutionHandle> AcquireAsync(string? solution, string workingDirectory, CancellationToken ct)
    {
        string solutionPath = ModelPipeline.DiscoverSolution(solution, workingDirectory);
        var diagnostics = new List<string>();
        LoadedSolution loaded = await WorkspaceLoader.LoadAsync(solutionPath, diagnostics.Add, ct);
        return new SolutionHandle(loaded.Solution, solutionPath, diagnostics, loaded);
    }
}