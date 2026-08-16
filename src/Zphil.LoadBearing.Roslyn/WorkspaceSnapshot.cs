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
    ///     incremental fragment store) treats a generation change as "flush and re-extract
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

    /// <summary>
    ///     The target framework each multi-target-framework project was loaded for, keyed by
    ///     <see cref="ProjectId" /> — the discriminator
    ///     <see cref="SolutionExtensions.NormalizeProjectNames" /> took out of the project names, carried
    ///     from the load that produced this snapshot. Empty for a solution whose projects each target one
    ///     framework.
    /// </summary>
    /// <remarks>
    ///     Stable for the whole <see cref="Generation" />: a <see cref="ProjectId" /> survives
    ///     <see cref="Solution.WithDocumentText(DocumentId,Microsoft.CodeAnalysis.Text.SourceText,PreservationMode)" />,
    ///     so the map a load produced stays valid across every in-place content edit folded into it.
    /// </remarks>
    internal IReadOnlyDictionary<ProjectId, string> TargetFrameworks { get; init; } = TargetFrameworkMaps.None;

    /// <summary>
    ///     The absolute <c>.csproj</c> paths of the projects that failed to load, from the load that produced
    ///     this snapshot — half of what the fail-closed gate keys on, where <see cref="Diagnostics" /> only
    ///     renders.
    /// </summary>
    /// <remarks>
    ///     Refreshed wholesale on each full (re)load and stable across the in-place content edits folded into
    ///     one generation, exactly as <see cref="Diagnostics" /> is: whether a project loaded is a property of
    ///     the load, and an edit that a sweep folds in never adds or removes a project.
    /// </remarks>
    internal IReadOnlyList<string> FailedProjects { get; init; } = [];

    /// <summary>
    ///     The absolute <c>.csproj</c> paths the solution declares that the load did not check — non-empty
    ///     only when the session is bound to a solution filter. It scopes the verdict rather than gating it.
    /// </summary>
    /// <remarks>
    ///     Refreshed and stable on exactly the same terms as <see cref="FailedProjects" />: which projects a
    ///     filter leaves out is a property of the load, and no content edit folded into a generation can
    ///     change it.
    /// </remarks>
    internal IReadOnlyList<string> UncheckedProjects { get; init; } = [];

    /// <summary>
    ///     The absolute <c>.csproj</c> paths of the projects whose NuGet packages are not in the model, from
    ///     the load that produced this snapshot — the gate's other input.
    /// </summary>
    /// <remarks>
    ///     Generation-scoped like its two siblings, and for a reason worth stating: a restore that repairs the
    ///     hole writes <c>project.assets.json</c>, which is a structural input the reconcile sweep already
    ///     watches — appearing where there was none is the same existence flip as changing — so it forces a
    ///     full reload, and the fact is recomputed there rather than going stale inside a generation. A
    ///     content edit folded into one generation cannot change it.
    /// </remarks>
    internal IReadOnlyList<string> RestoreFailedProjects { get; init; } = [];

    /// <summary>
    ///     This snapshot's load verdict as the one value every surface reads: the diagnostics and the three
    ///     project lists above, bundled so no consumer re-pairs them. Merge notes are empty by construction —
    ///     only extraction produces them, and a snapshot describes a load.
    /// </summary>
    internal WorkspaceDiagnostics LoadDiagnostics =>
        new(Diagnostics, [], FailedProjects, UncheckedProjects, RestoreFailedProjects);
}
