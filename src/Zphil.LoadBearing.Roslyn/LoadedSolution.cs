using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Zphil.LoadBearing.Roslyn.Diagnostics;
using Zphil.LoadBearing.Roslyn.Extraction;
using Zphil.LoadBearing.Roslyn.Solutions;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     A loaded MSBuild solution paired with its owning workspace. Dispose to release the workspace
///     and its out-of-process BuildHost. <see cref="Solution" /> is the unresolved-reference-stripped,
///     project-name-normalized snapshot the extractor reads, and <see cref="TargetFrameworks" /> carries
///     the discriminators the normalization removed.
/// </summary>
public sealed class LoadedSolution : IDisposable
{
    internal LoadedSolution(
        MSBuildWorkspace workspace, Solution solution,
        IReadOnlyDictionary<ProjectId, string>? targetFrameworks = null,
        ProjectLoadReport? report = null,
        IReadOnlyList<string>? restoreFailedProjects = null)
    {
        Workspace = workspace;
        Solution = solution;
        TargetFrameworks = targetFrameworks ?? TargetFrameworkMaps.None;
        FailedProjects = (report ?? ProjectLoadReport.Empty).Failed;
        UncheckedProjects = (report ?? ProjectLoadReport.Empty).Unchecked;
        UnsupportedProjects = (report ?? ProjectLoadReport.Empty).Unsupported;
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
    ///     Its own slot rather than a fold into <see cref="FailedProjects" />: these projects <em>did</em>
    ///     load, and the remedy differs (<c>dotnet restore</c>, not <c>dotnet build</c>). The full rationale
    ///     rides the one value every surface reads — <see cref="WorkspaceDiagnostics" />' slot of the same
    ///     name.
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

    /// <summary>
    ///     The projects this solution declares that no extractor reaches, each with its
    ///     <see cref="UnsupportedProjectKind" /> and ordinal-sorted by path — read off the solution file
    ///     rather than the load, since a project that was never going to load leaves no shape in one. Empty
    ///     for a solution every project of which this product can read.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Never gates, on <see cref="UncheckedProjects" />' terms rather than
    ///         <see cref="FailedProjects" />': the model is smaller than the solution, not wrong about it.
    ///     </para>
    ///     <para>
    ///         <b>Internal where its three siblings are public</b>, because it alone carries a taxonomy. The
    ///         others are bare paths and say all they will ever say; this one's kinds are expected to grow as
    ///         project shapes this product cannot read are told apart, and a kind on a public surface would
    ///         be a compatibility promise about a set that is not finished. It travels the way
    ///         <see cref="WorkspaceDiagnostics" /> and its <c>MultiTargetedProject</c>s already do — visible
    ///         to the CLI, the adapter and the tests through <c>InternalsVisibleTo</c>, and to nobody else.
    ///     </para>
    /// </remarks>
    internal IReadOnlyList<UnsupportedProject> UnsupportedProjects { get; }

    /// <summary>
    ///     This load's verdict as the one value every surface reads — the four project lists above beside
    ///     the failure messages the load reported, which only the caller has: the loader writes them to a
    ///     sink it was handed, so they never land on this type. The merge's two facts — its notes and the
    ///     multi-targeted projects — are empty by construction, since only extraction produces them and none
    ///     has run at load time.
    /// </summary>
    /// <param name="loadFailures">The workspace-load failure messages this load wrote to the caller's sink.</param>
    internal WorkspaceDiagnostics LoadDiagnosticsWith(IReadOnlyList<string> loadFailures)
    {
        return new WorkspaceDiagnostics(
            loadFailures, [], FailedProjects, UncheckedProjects, RestoreFailedProjects, UnsupportedProjects, []);
    }

    /// <summary>Disposes the underlying workspace.</summary>
    public void Dispose()
    {
        Workspace.Dispose();
    }
}
