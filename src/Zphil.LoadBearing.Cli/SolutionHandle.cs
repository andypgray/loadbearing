using Microsoft.CodeAnalysis;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     What an <see cref="ISolutionSource" /> hands back: the loaded, unresolved-reference-stripped
///     <see cref="Solution" />, the discovered solution path, and the workspace-load diagnostics — plus
///     an optional <see cref="IDisposable" /> the handle owns (see <see cref="Dispose" />).
/// </summary>
/// <remarks>
///     A <see cref="Solution" /> stays usable after its workspace is disposed, so a handle read in flight is
///     safe even once a later call has reloaded.
/// </remarks>
internal sealed class SolutionHandle(
    Solution solution,
    string solutionPath,
    IReadOnlyList<string> diagnostics,
    IDisposable? owned,
    Func<IReadOnlyCollection<string>, CancellationToken, Task<SessionCodebase>>? warmCodebase = null) : IDisposable
{
    /// <summary>The loaded, unresolved-reference-stripped solution the command reads.</summary>
    public Solution Solution { get; } = solution;

    /// <summary>Absolute path to the discovered <c>.sln</c>/<c>.slnx</c>.</summary>
    public string SolutionPath { get; } = solutionPath;

    /// <summary>Workspace-load failure diagnostics, surfaced to stderr / the JSON document by the caller.</summary>
    public IReadOnlyList<string> Diagnostics { get; } = diagnostics;

    /// <summary>
    ///     The warm path's incremental codebase producer, or null on the cold/one-shot path. When present
    ///     (the warm MCP source), the extraction seam calls it instead of re-walking the whole solution: it
    ///     captures this call's snapshot plus the session's <see cref="SessionFragmentStore" />, so it reuses
    ///     clean projects' fragments, re-extracts only the dirty ∪ dependent set, and hands back the merged
    ///     model — memoized, so a call that re-walked nothing re-merges nothing either. It takes the caller's
    ///     excluded project names because the exclusion is applied at merge time, which is what lets one
    ///     store serve every tool whatever each drops. Null falls straight through to today's full
    ///     <c>ExtractFromSolutionAsync</c>, so the CLI path is unchanged.
    /// </summary>
    public Func<IReadOnlyCollection<string>, CancellationToken, Task<SessionCodebase>>? WarmCodebase { get; } =
        warmCodebase;

    /// <summary>
    ///     Disposes the owned workspace on the cold path; a no-op when the source owns nothing — a warm
    ///     session outlives the call and keeps its snapshot.
    /// </summary>
    public void Dispose()
    {
        owned?.Dispose();
    }
}
