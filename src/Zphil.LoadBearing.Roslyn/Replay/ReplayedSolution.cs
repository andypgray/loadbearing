using Basic.CompilerLog.Util;
using Microsoft.CodeAnalysis;

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
    private static readonly IReadOnlyDictionary<ProjectId, string> NoTargetFrameworks =
        new Dictionary<ProjectId, string>();

    private readonly SolutionReader _reader;

    internal ReplayedSolution(
        AdhocWorkspace workspace, SolutionReader reader, Solution solution,
        IReadOnlyDictionary<ProjectId, string>? targetFrameworks = null)
    {
        Workspace = workspace;
        _reader = reader;
        Solution = solution;
        TargetFrameworks = targetFrameworks ?? NoTargetFrameworks;
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

    /// <summary>Disposes the workspace and the binlog reader (releasing its stream and analyzer host).</summary>
    public void Dispose()
    {
        Workspace.Dispose();
        _reader.Dispose();
    }
}
