namespace Zphil.LoadBearing.Cli.Rendering;

// The wire shape of `graph --json` — the pre-spec codebase survey, its own document with its own
// schemaVersion (1), distinct from check and status. Serialized camelCase, indented, nulls omitted.
// Grouped counts only, never per-site dumps (the minimal-token posture); sites come later from `check`.
// The four optional slots below — grain, projectsScope, and the two workspace ones — are additive and null
// (omitted) on a full, unscoped survey whose workspace loaded, so the schema stays version 1 and the
// default document is byte-identical to the one before they existed. A run whose workspace did not load
// reaches this document only under --allow-workspace-diagnostics, since graph otherwise refuses before
// extraction.

/// <summary>The root <c>graph --json</c> document.</summary>
/// <param name="Grain">
///     <c>overview</c> when the namespace inventories were elided, <c>skeleton</c> when the
///     external-reference rows went with them, or null (omitted) at full grain — so a document that says
///     nothing about grain is the complete one, and a consumer can tell a coarser survey from a smaller
///     codebase without diffing it.
/// </param>
/// <param name="ProjectsScope">
///     The project-name globs the survey was narrowed to, or null (omitted) when it covers the whole
///     solution. Present, it explains why <c>projectEdges</c> can name a project <c>projects</c> does not:
///     an edge survives scoping on either endpoint.
/// </param>
/// <param name="WorkspaceDiagnostics">
///     The workspace-load diagnostics, or null (omitted) when there were none.
/// </param>
/// <param name="ModelIncomplete">
///     <see langword="true" /> when a project failed to load, so the survey below covers only what did load
///     — projects, types, and edges are all missing, not merely fewer; null (omitted) otherwise.
/// </param>
/// <param name="ExternalEdges">
///     The external-reference rows, or null (omitted) at skeleton grain — the one thing that grain elides
///     beyond overview's. Null here is "not rendered at this grain", never "none found": a solution with no
///     external references renders an empty array, and <see cref="ExternalEdgeCount" /> tells the two apart.
/// </param>
/// <param name="ExternalEdgeCount">
///     How many external-reference rows the elision dropped, present only when <see cref="ExternalEdges" />
///     is elided. Rendered so a coarser survey still says what is missing and how much of it there was,
///     rather than reading like a codebase with no external dependencies.
/// </param>
internal sealed record GraphJson(
    int SchemaVersion,
    string Solution,
    string? Grain,
    IReadOnlyList<string>? ProjectsScope,
    IReadOnlyList<GraphProjectJson> Projects,
    IReadOnlyList<GraphProjectEdgeJson> ProjectEdges,
    IReadOnlyList<GraphExternalEdgeJson>? ExternalEdges,
    int? ExternalEdgeCount,
    IReadOnlyList<string>? WorkspaceDiagnostics,
    bool? ModelIncomplete);

/// <summary>
///     One project: its declared references, solution-declared type count, and namespace inventory — the
///     last of which is null (omitted) at overview grain, being the one thing that grain elides.
/// </summary>
internal sealed record GraphProjectJson(
    string Name,
    IReadOnlyList<string> ProjectReferences,
    int Types,
    IReadOnlyList<GraphNamespaceJson>? Namespaces);

/// <summary>A namespace and the count of the project's declared types in it.</summary>
internal sealed record GraphNamespaceJson(string Namespace, int Types);

/// <summary>An observed cross-project reference edge with its distinct type-pair count.</summary>
internal sealed record GraphProjectEdgeJson(string Source, string Target, int References);

/// <summary>An external reference grouped by target namespace root, with its distinct type-pair count.</summary>
internal sealed record GraphExternalEdgeJson(string Source, string TargetNamespaceRoot, int References);
