using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     A loaded MSBuild solution paired with its owning workspace. Dispose to release the workspace
///     and its out-of-process BuildHost. <see cref="Solution" /> is the unresolved-reference-stripped
///     snapshot the extractor reads.
/// </summary>
public sealed class LoadedSolution : IDisposable
{
    internal LoadedSolution(MSBuildWorkspace workspace, Solution solution)
    {
        Workspace = workspace;
        Solution = solution;
    }

    /// <summary>The MSBuild workspace that produced <see cref="Solution" />.</summary>
    public MSBuildWorkspace Workspace { get; }

    /// <summary>The loaded, unresolved-reference-stripped solution.</summary>
    public Solution Solution { get; }

    /// <summary>Disposes the underlying workspace.</summary>
    public void Dispose()
    {
        Workspace.Dispose();
    }
}