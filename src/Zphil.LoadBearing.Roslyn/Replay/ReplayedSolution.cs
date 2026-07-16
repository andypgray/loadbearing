using Basic.CompilerLog.Util;
using Microsoft.CodeAnalysis;

namespace Zphil.LoadBearing.Roslyn.Replay;

/// <summary>
///     A binlog-replayed solution paired with the in-memory workspace it was loaded into — the replay
///     analog of <see cref="LoadedSolution" /> (Phase 12 D1). Unlike that type there is no MSBuild
///     <c>BuildHost</c> to release, because no design-time build ran: the structure came from a real
///     build's binlog and the source text is read from current disk. <see cref="Solution" /> is the
///     unresolved-reference-stripped snapshot the extractor reads, identical in kind to the MSBuild
///     path's output so downstream extraction, caching, and rendering are unaffected.
/// </summary>
/// <remarks>
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

    internal ReplayedSolution(AdhocWorkspace workspace, SolutionReader reader, Solution solution)
    {
        Workspace = workspace;
        _reader = reader;
        Solution = solution;
    }

    /// <summary>The in-memory workspace the replayed solution was added to.</summary>
    public AdhocWorkspace Workspace { get; }

    /// <summary>The replayed, unresolved-reference-stripped solution.</summary>
    public Solution Solution { get; }

    /// <summary>Disposes the workspace and the binlog reader (releasing its stream and analyzer host).</summary>
    public void Dispose()
    {
        Workspace.Dispose();
        _reader.Dispose();
    }
}