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
        IReadOnlyList<string>? unsupportedProjects = null)
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
    ///     <see cref="FailedProjects" />: there is no moment to read. A replay answers from a build that was
    ///     recorded, while <c>project.assets.json</c> — present, absent, or carrying an error — describes the
    ///     restore as it stands on disk <em>now</em>, so reading it here would stamp today's restore onto
    ///     yesterday's model, which is a claim neither file supports. That covers the never-restored arm as
    ///     well as the failed one, and more plainly: a tree cleaned since the capture has no assets files at
    ///     all, and blaming every SDK-style project in a replay for that would be reading disk to describe a
    ///     build that is not on it. A capture that was replayed because it was structurally valid is also, by
    ///     construction, a capture of a build that compiled, and a build does not compile through a resolution
    ///     failure of the kind this detects.
    /// </remarks>
    public IReadOnlyList<string> RestoreFailedProjects { get; } = [];

    /// <summary>
    ///     The absolute paths of the projects this capture built in a language the replay cannot read,
    ///     ordinal-sorted — the same fact <see cref="LoadedSolution.UnsupportedProjects" /> carries, so a
    ///     document says what it covers on both paths.
    /// </summary>
    /// <remarks>
    ///     Read from the capture rather than from a solution file, which is the only difference between the
    ///     two paths here: a binlog records what was built, so the compiler invocations the C#-only predicate
    ///     declines <em>are</em> the answer, and they are collected at the moment of declining them. A
    ///     project the build skipped entirely is therefore invisible — the replay analog of the MSBuild
    ///     path's undeclared-passenger limit.
    /// </remarks>
    public IReadOnlyList<string> UnsupportedProjects { get; }

    /// <summary>
    ///     This replay's verdict as the one value every surface reads — the three project lists above beside
    ///     the replay messages the caller's own sink collected, which never land on this type. Unchecked
    ///     projects are empty: a binlog records what was built, not what a solution declares, so there is
    ///     nothing to have left out. Merge notes are empty by construction, as on the MSBuild path.
    /// </summary>
    /// <param name="loadFailures">The replay messages this load wrote to the caller's sink.</param>
    internal WorkspaceDiagnostics LoadDiagnosticsWith(IReadOnlyList<string> loadFailures)
    {
        return new WorkspaceDiagnostics(
            loadFailures, [], FailedProjects, [], RestoreFailedProjects, UnsupportedProjects);
    }

    /// <summary>Disposes the workspace and the binlog reader (releasing its stream and analyzer host).</summary>
    public void Dispose()
    {
        Workspace.Dispose();
        _reader.Dispose();
    }
}
