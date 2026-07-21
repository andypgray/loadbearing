using Microsoft.CodeAnalysis;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     What an <see cref="ISolutionSource" /> hands back: the loaded, unresolved-reference-stripped
///     <see cref="Solution" />, the discovered solution path, and the workspace-load diagnostics — plus
///     an optional <see cref="IDisposable" /> the handle owns. The cold source owns the underlying MSBuild
///     workspace, so disposing the handle disposes it; the warm source owns nothing, so disposal is a
///     no-op (the session outlives the call and keeps the snapshot). A <see cref="Solution" /> stays usable
///     after its workspace is disposed, so a handle read in flight is safe even once a later call has
///     reloaded.
/// </summary>
internal sealed class SolutionHandle(
    Solution solution,
    string solutionPath,
    IReadOnlyList<string> diagnostics,
    IDisposable? owned,
    Func<CancellationToken, Task<SessionFragmentSet>>? warmFragments = null) : IDisposable
{
    /// <summary>The loaded, unresolved-reference-stripped solution the command reads.</summary>
    public Solution Solution { get; } = solution;

    /// <summary>Absolute path to the discovered <c>.sln</c>/<c>.slnx</c>.</summary>
    public string SolutionPath { get; } = solutionPath;

    /// <summary>Workspace-load failure diagnostics, surfaced to stderr / the JSON document by the caller.</summary>
    public IReadOnlyList<string> Diagnostics { get; } = diagnostics;

    /// <summary>
    ///     The warm path's incremental fragment extractor, or null on the cold/one-shot path. When present
    ///     (the warm MCP source), the extraction seam calls it instead of re-walking the whole
    ///     solution: it captures this call's snapshot plus the session's <see cref="SessionFragmentStore" />,
    ///     so it reuses clean projects' fragments and re-extracts only the dirty ∪ dependent set. Null falls
    ///     straight through to today's full <c>ExtractFromSolutionAsync</c>, so the CLI path is unchanged.
    /// </summary>
    public Func<CancellationToken, Task<SessionFragmentSet>>? WarmFragments { get; } = warmFragments;

    /// <summary>Disposes the owned workspace on the cold path; a no-op when the source owns nothing.</summary>
    public void Dispose()
    {
        owned?.Dispose();
    }
}