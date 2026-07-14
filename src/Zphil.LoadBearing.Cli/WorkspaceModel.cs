using Microsoft.CodeAnalysis;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The product of the shared workspace prefix (<see cref="ModelPipeline.LoadWithWorkspaceAsync" />):
///     the finalized model, the live solution the caller extracts from, the spec resolution (for the
///     exclude-project name), the workspace diagnostics, and the discovered solution path. Disposable —
///     it owns the <see cref="LoadedSolution" />; a <c>using</c> in the caller bounds the workspace
///     lifetime. The <see cref="Model" /> roots its own <c>Type</c> references, so it stays usable after
///     disposal.
/// </summary>
internal sealed class WorkspaceModel(
    LoadedSolution loaded,
    ArchitectureModel model,
    SpecResolution resolution,
    IReadOnlyList<string> diagnostics,
    string solutionPath) : IDisposable
{
    public LoadedSolution Loaded { get; } = loaded;

    public Solution Solution => Loaded.Solution;

    public ArchitectureModel Model { get; } = model;

    public SpecResolution Resolution { get; } = resolution;

    public IReadOnlyList<string> Diagnostics { get; } = diagnostics;

    public string SolutionPath { get; } = solutionPath;

    public string SolutionDirectory => Path.GetDirectoryName(SolutionPath)!;

    public void Dispose()
    {
        Loaded.Dispose();
    }
}