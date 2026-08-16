using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     A loaded MSBuild solution paired with its owning workspace. Dispose to release the workspace
///     and its out-of-process BuildHost. <see cref="Solution" /> is the unresolved-reference-stripped,
///     project-name-normalized snapshot the extractor reads, and <see cref="TargetFrameworks" /> carries
///     the discriminators the normalization removed.
/// </summary>
public sealed class LoadedSolution : IDisposable
{
    private static readonly IReadOnlyDictionary<ProjectId, string> NoTargetFrameworks =
        new Dictionary<ProjectId, string>();

    internal LoadedSolution(
        MSBuildWorkspace workspace, Solution solution,
        IReadOnlyDictionary<ProjectId, string>? targetFrameworks = null,
        ProjectLoadReport? report = null)
    {
        Workspace = workspace;
        Solution = solution;
        TargetFrameworks = targetFrameworks ?? NoTargetFrameworks;
        FailedProjects = (report ?? ProjectLoadReport.Empty).Failed;
        UncheckedProjects = (report ?? ProjectLoadReport.Empty).Unchecked;
    }

    /// <summary>The MSBuild workspace that produced <see cref="Solution" />.</summary>
    public MSBuildWorkspace Workspace { get; }

    /// <summary>The loaded, unresolved-reference-stripped solution.</summary>
    public Solution Solution { get; }

    /// <summary>
    ///     The target framework each multi-target-framework project was loaded for, keyed by
    ///     <see cref="ProjectId" /> — the discriminator
    ///     <see cref="SolutionExtensions.NormalizeProjectNames" /> took out of the project names. Empty for a
    ///     solution whose projects each target one framework.
    /// </summary>
    public IReadOnlyDictionary<ProjectId, string> TargetFrameworks { get; }

    /// <summary>
    ///     The absolute <c>.csproj</c> paths of the projects that failed to load, ordinal-sorted — what the
    ///     fail-closed gate keys on, computed at this boundary by
    ///     <see cref="ProjectLoadFailures.Detect" />. Empty for a solution that loaded completely.
    /// </summary>
    public IReadOnlyList<string> FailedProjects { get; }

    /// <summary>
    ///     The absolute <c>.csproj</c> paths this solution declares that the run did not check, ordinal-sorted
    ///     — non-empty only when the load went through a <c>.slnf</c> that left members out. A narrowed
    ///     universe is a smaller true answer rather than a broken one, so unlike
    ///     <see cref="FailedProjects" /> this never gates; it is what keeps a green over a subset from
    ///     reading as a green over the solution.
    /// </summary>
    public IReadOnlyList<string> UncheckedProjects { get; }

    /// <summary>Disposes the underlying workspace.</summary>
    public void Dispose()
    {
        Workspace.Dispose();
    }
}
