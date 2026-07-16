using Microsoft.CodeAnalysis;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The product of the shared workspace prefix
///     (<see cref="ModelPipeline.LoadWithWorkspaceAsync(ISolutionSource, string, string, string, CancellationToken)" />):
///     the finalized model, the spec resolution (for the exclude-project name), and — read through the
///     <see cref="SolutionHandle" /> it wraps — the live solution the caller extracts from, the workspace
///     diagnostics, and the discovered solution path. Disposable: disposing releases whatever the handle
///     owns (the cold one-shot workspace) and no-ops on the warm session's shared snapshot; a <c>using</c>
///     in the caller bounds the cold workspace lifetime. The <see cref="Model" /> roots its own
///     <c>Type</c> references, so it stays usable after disposal.
/// </summary>
internal sealed class WorkspaceModel(
    SolutionHandle handle,
    ArchitectureModel model,
    SpecResolution resolution) : IDisposable
{
    public Solution Solution => handle.Solution;

    public ArchitectureModel Model { get; } = model;

    public SpecResolution Resolution { get; } = resolution;

    public IReadOnlyList<string> Diagnostics => handle.Diagnostics;

    public string SolutionPath => handle.SolutionPath;

    public string SolutionDirectory => Path.GetDirectoryName(SolutionPath)!;

    public void Dispose()
    {
        handle.Dispose();
    }
}