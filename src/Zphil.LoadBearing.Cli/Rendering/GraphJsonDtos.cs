namespace Zphil.LoadBearing.Cli.Rendering;

// The wire shape of `graph --json` — the pre-spec codebase survey, its own document with its own
// schemaVersion (1), distinct from check and status. Serialized camelCase, indented, nulls omitted.
// Grouped counts only, never per-site dumps (the minimal-token posture); sites come later from `check`.

/// <summary>The root <c>graph --json</c> document.</summary>
internal sealed record GraphJson(
    int SchemaVersion,
    string Solution,
    IReadOnlyList<GraphProjectJson> Projects,
    IReadOnlyList<GraphProjectEdgeJson> ProjectEdges,
    IReadOnlyList<GraphExternalEdgeJson> ExternalEdges);

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