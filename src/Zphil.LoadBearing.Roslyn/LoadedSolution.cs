using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Zphil.LoadBearing.Roslyn.Diagnostics;
using Zphil.LoadBearing.Roslyn.Extraction;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     A loaded MSBuild solution together with the workspace that produced it. <see cref="Solution" /> is
///     what <see cref="CodebaseExtractor.ExtractFromSolutionAsync" /> reads: references that did not
///     resolve have been dropped, and a multi-target-framework project's several compilations have been
///     given one project name, with the framework each was loaded for in <see cref="TargetFrameworks" />.
///     The project lists below say how completely it loaded. Dispose it to release the workspace and its
///     out-of-process build host; the <see cref="Solution" /> stays readable afterwards.
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

    /// <summary>Gets the MSBuild workspace that produced <see cref="Solution" />.</summary>
    public MSBuildWorkspace Workspace { get; }

    /// <summary>
    ///     Gets the loaded solution: references that did not resolve have been dropped, and a multi-target-framework
    ///     project's several compilations all carry the one project name, told apart by
    ///     <see cref="TargetFrameworks" />.
    /// </summary>
    public Solution Solution { get; }

    /// <summary>
    ///     Gets the target framework each multi-target-framework project was loaded for, keyed by
    ///     <see cref="ProjectId" />. Such a project arrives as one <see cref="Project" /> per framework, all
    ///     sharing the one project name, and this map is what tells them apart; pass it to
    ///     <see cref="CodebaseExtractor.ExtractFromSolutionAsync" /> so the extracted model records which
    ///     framework a fact came from. Empty for a solution whose projects each target one framework.
    /// </summary>
    public IReadOnlyDictionary<ProjectId, string> TargetFrameworks { get; }

    /// <summary>
    ///     Gets the absolute <c>.csproj</c> paths of the projects that failed to load, ordinal-sorted. A
    ///     non-empty list means the model built from this solution is missing whatever those projects declare,
    ///     so report it rather than let a clean result read as a clean solution; the remedy is
    ///     <c>dotnet build</c>. Empty for a solution that loaded completely.
    /// </summary>
    public IReadOnlyList<string> FailedProjects { get; }

    /// <summary>
    ///     Gets the absolute <c>.csproj</c> paths of the projects whose NuGet packages are not in the model,
    ///     ordinal-sorted: restore ran and failed, or never ran. These projects did load, so the types they
    ///     declare are present while the types they get from packages are not, and the remedy is
    ///     <c>dotnet restore</c> rather than <c>dotnet build</c>. Never overlaps
    ///     <see cref="FailedProjects" />. Empty for a solution that restored cleanly.
    /// </summary>
    public IReadOnlyList<string> RestoreFailedProjects { get; }

    /// <summary>
    ///     Gets the absolute <c>.csproj</c> paths this solution declares that the load did not read,
    ///     ordinal-sorted — non-empty only when what was loaded is a <c>.slnf</c> filter that leaves members
    ///     out. Unlike <see cref="FailedProjects" /> this is not a fault: the model is a true answer about a
    ///     smaller set of projects. Report it, or a clean result over a subset reads as a clean result over the
    ///     solution.
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

    /// <summary>
    ///     Disposes the MSBuild workspace and its out-of-process build host. <see cref="Solution" /> stays
    ///     readable afterwards.
    /// </summary>
    public void Dispose()
    {
        Workspace.Dispose();
    }
}
