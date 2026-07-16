namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     The extraction of one <see cref="CompilationInput" /> (one project, one target framework) as pure,
///     self-contained data: the project's name and forward references, the types it declares, the
///     externals it references, and its reference edges. It holds no Roslyn types (no <c>ISymbol</c>,
///     <c>Compilation</c>, or <c>Location</c>) so it is System.Text.Json-serializable by design — the
///     persisted extraction cache (Phase 11 WP6) stores exactly this — and
///     <see cref="FragmentMerger.Merge" /> reconstructs a <see cref="Zphil.LoadBearing.Codebase.CodebaseModel" />
///     from a set of fragments, reproducing the global three-pass cross-input semantics.
/// </summary>
/// <remarks>
///     A single fragment is one compilation's <em>narrow view</em>: a type another project declares still
///     appears in its <see cref="Externals" /> (as metadata), and the merge — not the fragment — decides
///     unification. The fragment's own collections are materialized in a canonical order (declared types
///     and externals ordinal by FQN, edges by source then target) so serialization is stable; global
///     ordering is re-derived at merge, so a fragment's internal order never affects the model.
/// </remarks>
internal sealed record CodebaseFragment(
    string ProjectName,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<FragmentType> DeclaredTypes,
    IReadOnlyList<FragmentExternal> Externals,
    IReadOnlyList<FragmentEdge> Edges);