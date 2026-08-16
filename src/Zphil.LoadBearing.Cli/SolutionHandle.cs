using Microsoft.CodeAnalysis;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     What an <see cref="ISolutionSource" /> hands back: the loaded, unresolved-reference-stripped
///     <see cref="Solution" />, the discovered solution path, the workspace-load diagnostics, the projects
///     that failed to load and the ones a filter left unchecked — plus an optional
///     <see cref="IDisposable" /> the handle owns (see <see cref="Dispose" />).
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
    Func<IReadOnlyCollection<string>, CancellationToken, Task<SessionCodebase>>? warmCodebase = null,
    IReadOnlyDictionary<ProjectId, string>? targetFrameworks = null,
    IReadOnlyList<string>? failedProjects = null,
    IReadOnlyList<string>? uncheckedProjects = null) : IDisposable
{
    private static readonly IReadOnlyDictionary<ProjectId, string> NoTargetFrameworks =
        new Dictionary<ProjectId, string>();

    /// <summary>The loaded, unresolved-reference-stripped solution the command reads.</summary>
    public Solution Solution { get; } = solution;

    /// <summary>
    ///     The target framework each multi-target-framework project was loaded for, keyed by
    ///     <see cref="ProjectId" /> — what the extraction stamps onto its fragments so the merge can name the
    ///     framework whose facts won. Empty for a solution whose projects each target one framework.
    /// </summary>
    public IReadOnlyDictionary<ProjectId, string> TargetFrameworks { get; } = targetFrameworks ?? NoTargetFrameworks;

    /// <summary>Absolute path to the discovered <c>.sln</c>/<c>.slnx</c>, or to the <c>.slnf</c> filtering one.</summary>
    public string SolutionPath { get; } = solutionPath;

    /// <summary>Workspace-load failure diagnostics, surfaced to stderr / the JSON document by the caller.</summary>
    public IReadOnlyList<string> Diagnostics { get; } = diagnostics;

    /// <summary>
    ///     The absolute <c>.csproj</c> paths of the projects that failed to load — the fail-closed gate's
    ///     whole input, where <see cref="Diagnostics" /> is only what gets rendered beside it.
    /// </summary>
    public IReadOnlyList<string> FailedProjects { get; } = failedProjects ?? [];

    /// <summary>
    ///     The absolute <c>.csproj</c> paths the solution declares that this run did not check — non-empty
    ///     only under a solution filter. It scopes the verdict and never gates it, which is exactly why it is
    ///     carried separately from <see cref="FailedProjects" /> rather than folded in.
    /// </summary>
    public IReadOnlyList<string> UncheckedProjects { get; } = uncheckedProjects ?? [];

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
