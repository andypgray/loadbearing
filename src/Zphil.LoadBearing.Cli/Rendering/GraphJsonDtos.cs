namespace Zphil.LoadBearing.Cli.Rendering;

// The wire shape of `graph --json` — the pre-spec codebase survey, its own document with its own
// schemaVersion (1), distinct from check and status. Serialized camelCase, indented, nulls omitted.
// Grouped counts only, never per-site dumps (the minimal-token posture); sites come later from `check`.
// The two workspace slots below are additive and null (omitted) on every run whose workspace loaded — and a
// run whose workspace did not load reaches this document only under --allow-workspace-diagnostics, since
// graph otherwise refuses before extraction — so the schema stays version 1 and a clean survey is
// byte-identical.

/// <summary>The root <c>graph --json</c> document.</summary>
/// <param name="WorkspaceDiagnostics">
///     The workspace-load diagnostics, or null (omitted) when there were none.
/// </param>
/// <param name="ModelIncomplete">
///     <see langword="true" /> when a project failed to load, so the survey below covers only what did load
///     — projects, types, and edges are all missing, not merely fewer; null (omitted) otherwise.
/// </param>
internal sealed record GraphJson(
    int SchemaVersion,
    string Solution,
    IReadOnlyList<GraphProjectJson> Projects,
    IReadOnlyList<GraphProjectEdgeJson> ProjectEdges,
    IReadOnlyList<GraphExternalEdgeJson> ExternalEdges,
    IReadOnlyList<string>? WorkspaceDiagnostics,
    bool? ModelIncomplete);

/// <summary>One project: its declared references, solution-declared type count, and namespace inventory.</summary>
internal sealed record GraphProjectJson(
    string Name,
    IReadOnlyList<string> ProjectReferences,
    int Types,
    IReadOnlyList<GraphNamespaceJson> Namespaces);

/// <summary>A namespace and the count of the project's declared types in it.</summary>
internal sealed record GraphNamespaceJson(string Namespace, int Types);

/// <summary>An observed cross-project reference edge with its distinct type-pair count.</summary>
internal sealed record GraphProjectEdgeJson(string Source, string Target, int References);

/// <summary>An external reference grouped by target namespace root, with its distinct type-pair count.</summary>
internal sealed record GraphExternalEdgeJson(string Source, string TargetNamespaceRoot, int References);
