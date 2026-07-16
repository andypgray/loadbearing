using Microsoft.CodeAnalysis;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     An immutable view of a <see cref="WorkspaceSession" />'s solution as of one
///     <see cref="WorkspaceSession.GetCurrentAsync" /> call: the reconciled <see cref="Solution" /> and the
///     workspace-load diagnostics that describe it.
/// </summary>
/// <remarks>
///     The <see cref="Solution" /> is a Roslyn immutable snapshot and stays usable even after the session
///     reloads or is disposed (a solution outlives its owning workspace), so a snapshot can be read
///     concurrently with a later reconcile. <see cref="Diagnostics" /> is refreshed wholesale on each full
///     (re)load and is stable across in-place content edits, since it describes the loaded workspace rather
///     than any single document.
/// </remarks>
/// <param name="Solution">The reconciled, unresolved-reference-stripped solution.</param>
/// <param name="Diagnostics">Workspace-load failure messages captured during the load that produced this snapshot.</param>
public sealed record WorkspaceSnapshot(Solution Solution, IReadOnlyList<string> Diagnostics)
{
    private static readonly IReadOnlyDictionary<string, int> NoEditVersions =
        new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>
    ///     The session load generation that produced this snapshot — bumped on every full (re)load, stable
    ///     across the in-place content edits folded into the same load. A session-scoped consumer (the
    ///     incremental fragment store, Phase 12 D2) treats a generation change as "flush and re-extract
    ///     everything" and reuses its work only within a generation. Internal, non-positional, so the record
    ///     stays publicly <c>(Solution, Diagnostics)</c>; visible to the CLI and tests via InternalsVisibleTo.
    /// </summary>
    internal long Generation { get; init; }

    /// <summary>
    ///     Per-project (by name) monotonic edit counters within <see cref="Generation" />: a project's value
    ///     rises each time the reconcile sweep rewrites one of its documents. Comparing this map against the
    ///     one it last extracted at tells a session-scoped consumer exactly which projects to re-walk.
    /// </summary>
    /// <remarks>
    ///     <b>Why the session's own content delta, not a Roslyn version.</b> Identity here is deliberately the
    ///     bytes the sweep actually rewrote, not <see cref="Solution.GetChanges" /> or a semantic version:
    ///     extracted fragments carry <c>file:line</c> sites, so a comment-only edit that shifts line numbers
    ///     without changing any symbol must still dirty its project — and the sweep already knows that delta at
    ///     zero extra I/O. A semantic-version identity would miss the line shift and strand stale sites.
    /// </remarks>
    internal IReadOnlyDictionary<string, int> ProjectEditVersions { get; init; } = NoEditVersions;
}