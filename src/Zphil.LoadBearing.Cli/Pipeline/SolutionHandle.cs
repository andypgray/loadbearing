using Microsoft.CodeAnalysis;
using Zphil.LoadBearing.Cli.SpecLoading;
using Zphil.LoadBearing.Roslyn.Diagnostics;
using Zphil.LoadBearing.Roslyn.Extraction;

namespace Zphil.LoadBearing.Cli.Pipeline;

/// <summary>
///     What an <see cref="ISolutionSource" /> hands back: the loaded, unresolved-reference-stripped
///     <see cref="Solution" />, the discovered solution path and how well the workspace loaded
///     (<see cref="LoadDiagnostics" />) — plus an optional <see cref="IDisposable" /> the handle owns (see
///     <see cref="Dispose" />).
/// </summary>
/// <remarks>
///     A <see cref="Solution" /> stays usable after its workspace is disposed, so a handle read in flight is
///     safe even once a later call has reloaded.
/// </remarks>
internal sealed class SolutionHandle(
    Solution solution,
    string solutionPath,
    WorkspaceDiagnostics loadDiagnostics,
    IDisposable? owned,
    Func<IReadOnlyCollection<string>, IReadOnlySet<string>?, CancellationToken, Task<SessionCodebase>>? warmCodebase = null,
    Func<string, Func<SessionSpecResolution>, SessionSpecResolution>? warmSpecResolution = null,
    IReadOnlyDictionary<ProjectId, string>? targetFrameworks = null) : IDisposable
{
    /// <summary>The loaded, unresolved-reference-stripped solution the command reads.</summary>
    public Solution Solution { get; } = solution;

    /// <summary>
    ///     The target framework each multi-target-framework project was loaded for, keyed by
    ///     <see cref="ProjectId" /> — what the extraction stamps onto its fragments so the merge can name the
    ///     framework whose facts won. Empty for a solution whose projects each target one framework.
    /// </summary>
    public IReadOnlyDictionary<ProjectId, string> TargetFrameworks { get; } =
        targetFrameworks ?? TargetFrameworkMaps.None;

    /// <summary>Absolute path to the discovered <c>.sln</c>/<c>.slnx</c>, or to the <c>.slnf</c> filtering one.</summary>
    public string SolutionPath { get; } = solutionPath;

    /// <summary>
    ///     How well the workspace loaded, as the one value the source computed: the failure diagnostics the
    ///     caller renders, the projects that failed to load, the ones whose NuGet packages are not in the
    ///     model and the ones a filter left unchecked. Merge notes are empty here — only extraction produces
    ///     them, and a handle is what a load hands back.
    /// </summary>
    public WorkspaceDiagnostics LoadDiagnostics { get; } = loadDiagnostics;

    /// <summary>
    ///     The warm path's incremental codebase producer, or null on the cold/one-shot path. When present
    ///     (the warm MCP source), the extraction seam calls it instead of re-walking the whole solution: it
    ///     captures this call's snapshot plus the session's <see cref="SessionFragmentStore" />, so it reuses
    ///     clean projects' fragments, re-extracts only the dirty ∪ dependent set, and hands back the merged
    ///     model — memoized, so a call that re-walked nothing re-merges nothing either. It takes the caller's
    ///     excluded project names because the exclusion is applied at merge time, which is what lets one
    ///     store serve every tool whatever each drops, and the caller's declared solution membership because
    ///     the read belongs to the one seam that already knows the solution path. Null falls through to the
    ///     CLI's own one-walk-per-run path, which memoizes the same two tiers over the fragments it walked.
    /// </summary>
    public Func<IReadOnlyCollection<string>, IReadOnlySet<string>?, CancellationToken, Task<SessionCodebase>>?
        WarmCodebase { get; } = warmCodebase;

    /// <summary>
    ///     The warm path's spec-resolution memo, or null on the cold/one-shot path. When present (the warm
    ///     MCP source), the resolution seam hands it the normalized <c>--spec</c> argument and the cold
    ///     resolution as a callback, and gets back either a replay of the resolution this session already
    ///     computed under the same load generation or the result of running that callback. It captures the
    ///     session's <see cref="SpecResolutionCache" /> and this call's generation, which is what makes an
    ///     edit to a <c>.cs</c> file free and a structural change a miss. Null falls straight through to the
    ///     callback, so the CLI path resolves exactly as it always did.
    /// </summary>
    /// <remarks>
    ///     The cold resolution goes down as a callback rather than the seam handing over its inputs, so
    ///     everything resolution knows — which spec argument means what, which membership read serves both
    ///     halves, how a failure surfaces — stays at that one seam, and the warm side contributes only the
    ///     memo and the generation to stamp it with.
    /// </remarks>
    public Func<string, Func<SessionSpecResolution>, SessionSpecResolution>? WarmSpecResolution { get; } =
        warmSpecResolution;

    /// <summary>
    ///     Disposes the owned workspace on the cold path; a no-op when the source owns nothing — a warm
    ///     session outlives the call and keeps its snapshot.
    /// </summary>
    public void Dispose()
    {
        owned?.Dispose();
    }
}
