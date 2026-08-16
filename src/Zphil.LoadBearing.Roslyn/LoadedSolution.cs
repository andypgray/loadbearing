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
        ProjectLoadReport? report = null,
        IReadOnlyList<string>? restoreFailedProjects = null)
    {
        Workspace = workspace;
        Solution = solution;
        TargetFrameworks = targetFrameworks ?? NoTargetFrameworks;
        FailedProjects = (report ?? ProjectLoadReport.Empty).Failed;
        UncheckedProjects = (report ?? ProjectLoadReport.Empty).Unchecked;
        RestoreFailedProjects = restoreFailedProjects ?? [];
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
    ///     The absolute <c>.csproj</c> paths of the projects that failed to load, ordinal-sorted — one of the
    ///     two facts the fail-closed gate keys on, computed at this boundary by
    ///     <see cref="ProjectLoadFailures.Detect" />. Empty for a solution that loaded completely.
    /// </summary>
    public IReadOnlyList<string> FailedProjects { get; }

    /// <summary>
    ///     The absolute <c>.csproj</c> paths of the projects whose NuGet packages are not in the model —
    ///     restore ran and failed, or never ran — ordinal-sorted. The gate's other input, computed at this
    ///     boundary by <see cref="RestoreFailures.Detect" />. Empty for a solution that restored cleanly, and
    ///     disjoint from <see cref="FailedProjects" /> by construction.
    /// </summary>
    /// <remarks>
    ///     Carried in its own slot rather than folded into <see cref="FailedProjects" /> because these projects
    ///     <em>did</em> load — naming them as "failed to load" would be false — and because the remedy differs:
    ///     <c>dotnet restore</c>, rather than <c>dotnet build</c>. The two causes share this one slot for the
    ///     same reason, from the other direction: they ask for the same command and leave the same hole, and a
    ///     consumer that could tell them apart would have nothing different to do about it. What they share
    ///     with each other is the consequence: every package edge these projects declare is missing from the
    ///     model, so a rule over one is measured against edges that were never extracted.
    /// </remarks>
    public IReadOnlyList<string> RestoreFailedProjects { get; }

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
