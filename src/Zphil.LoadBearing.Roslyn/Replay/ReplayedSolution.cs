using Basic.CompilerLog.Util;
using Microsoft.CodeAnalysis;
using Zphil.LoadBearing.Roslyn.Diagnostics;
using Zphil.LoadBearing.Roslyn.Extraction;

namespace Zphil.LoadBearing.Roslyn.Replay;

/// <summary>
///     A binlog-replayed solution paired with the in-memory workspace it was loaded into — the replay
///     analog of <see cref="LoadedSolution" />. <see cref="Solution" /> is the
///     unresolved-reference-stripped snapshot, identical in kind to the MSBuild path's output.
/// </summary>
/// <remarks>
///     Unlike <see cref="LoadedSolution" /> there is no MSBuild <c>BuildHost</c> to release, because no
///     design-time build ran: the structure came from a real build's binlog and the source text is read
///     from current disk.
///     Owns two disposables: the <see cref="AdhocWorkspace" /> and the <see cref="SolutionReader" />
///     whose lazy per-document text loaders back the solution's source (and whose analyzer host holds
///     the on-disk analyzer assemblies). A Roslyn <see cref="Solution" /> outlives the workspace that
///     produced it — the same contract <see cref="LoadedSolution" /> keeps everywhere in this repo — so
///     callers may keep reading a <see cref="Solution" /> already materialised by extraction after
///     <see cref="Dispose" />; disposal only frees the workspace and reader resources.
/// </remarks>
internal sealed class ReplayedSolution : IDisposable
{
    private readonly SolutionReader _reader;

    internal ReplayedSolution(
        AdhocWorkspace workspace, SolutionReader reader, Solution solution,
        IReadOnlyDictionary<ProjectId, string>? targetFrameworks = null,
        IReadOnlyList<string>? failedProjects = null,
        IReadOnlyList<UnsupportedProject>? unsupportedProjects = null)
    {
        Workspace = workspace;
        _reader = reader;
        Solution = solution;
        TargetFrameworks = targetFrameworks ?? TargetFrameworkMaps.None;
        FailedProjects = failedProjects ?? [];
        UnsupportedProjects = unsupportedProjects ?? [];
    }

    /// <summary>The in-memory workspace the replayed solution was added to.</summary>
    public AdhocWorkspace Workspace { get; }

    /// <summary>The replayed, unresolved-reference-stripped solution.</summary>
    public Solution Solution { get; }

    /// <summary>
    ///     The target framework each multi-target-framework project was built for, keyed by
    ///     <see cref="ProjectId" />, read from the recorded output paths (the replay reader applies no name
    ///     discriminator of its own). Empty for a solution whose projects each target one framework, and for
    ///     any project that suppresses the framework segment of its output path.
    /// </summary>
    public IReadOnlyDictionary<ProjectId, string> TargetFrameworks { get; }

    /// <summary>
    ///     The absolute <c>.csproj</c> paths of the projects that failed to load — the same fact
    ///     <see cref="LoadedSolution.FailedProjects" /> carries, so the gate reads one thing on both paths.
    /// </summary>
    /// <remarks>
    ///     Empty by construction on this path, and pinned so: a replayed project comes from a compiler
    ///     invocation a real build actually made, so it carries an output path and cannot present the empty
    ///     shape. There is also no solution file here to read declared membership from — a binlog records
    ///     what was built, not what a <c>.sln</c> declares — so the declared-but-absent arm has nothing to
    ///     compare against and is skipped.
    /// </remarks>
    public IReadOnlyList<string> FailedProjects { get; }

    /// <summary>
    ///     The absolute <c>.csproj</c> paths of the projects whose NuGet packages are not in the model — the
    ///     same fact <see cref="LoadedSolution.RestoreFailedProjects" /> carries, so the gate reads one thing
    ///     on both paths.
    /// </summary>
    /// <remarks>
    ///     Empty by construction on this path, and pinned so for a different reason than
    ///     <see cref="FailedProjects" />: there is no moment to read. An assets file describes the restore
    ///     as it stands on disk <em>now</em>, so reading one here would stamp today's restore onto
    ///     yesterday's model — and a capture that was replayed because it was structurally valid is, by
    ///     construction, a capture of a build that compiled.
    /// </remarks>
    public IReadOnlyList<string> RestoreFailedProjects { get; } = [];

    /// <summary>
    ///     The projects this capture built in a language the replay cannot read, ordinal-sorted by path —
    ///     the same fact <see cref="LoadedSolution.UnsupportedProjects" /> carries, so a document says what
    ///     it covers on both paths, and internal for that property's reason.
    /// </summary>
    /// <remarks>
    ///     Read from the capture rather than from a solution file, which is the only difference between the
    ///     two paths here: a binlog records what was built, so the compiler invocations the C#-only predicate
    ///     declines <em>are</em> the answer, and they are collected at the moment of declining them. Every
    ///     entry is therefore <see cref="UnsupportedProjectKind.NotCsharp" />, and that is a property of the
    ///     evidence rather than a simplification: this path sees compilations, and a shared project is not
    ///     one. A project the build skipped entirely is invisible here — the replay analog of the MSBuild
    ///     path's undeclared-passenger limit.
    /// </remarks>
    internal IReadOnlyList<UnsupportedProject> UnsupportedProjects { get; }

    /// <summary>
    ///     This replay's verdict as the one value every surface reads — the three project lists above beside
    ///     the replay messages the caller's own sink collected, which never land on this type. Unchecked
    ///     projects are empty: a binlog records what was built, not what a solution declares, so there is
    ///     nothing to have left out. The merge's two facts are empty by construction, as on the MSBuild path.
    /// </summary>
    /// <param name="loadFailures">The replay messages this load wrote to the caller's sink.</param>
    internal WorkspaceDiagnostics LoadDiagnosticsWith(IReadOnlyList<string> loadFailures)
    {
        return new WorkspaceDiagnostics(
            loadFailures, [], FailedProjects, [], RestoreFailedProjects, UnsupportedProjects, []);
    }

    /// <summary>Disposes the workspace and the binlog reader (releasing its stream and analyzer host).</summary>
    public void Dispose()
    {
        Workspace.Dispose();
        _reader.Dispose();
    }
}
