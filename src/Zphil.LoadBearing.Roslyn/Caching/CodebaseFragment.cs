namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     The extraction of one <see cref="CompilationInput" /> (one project, one target framework) as pure,
///     self-contained data. It holds no Roslyn types (no <c>ISymbol</c>, <c>Compilation</c>, or
///     <c>Location</c>) so it is System.Text.Json-serializable by design — the persisted extraction cache
///     stores exactly this — and <see cref="FragmentMerger.Merge" /> reconstructs a
///     <see cref="Zphil.LoadBearing.Codebase.CodebaseModel" /> from a set of fragments, reproducing the
///     global cross-input semantics.
/// </summary>
/// <remarks>
///     A single fragment is one compilation's <em>narrow view</em>: a type another project declares still
///     appears in its <see cref="Externals" /> (as metadata), and the merge — not the fragment — decides
///     unification. The fragment's own collections are materialized in a canonical order (declared types
///     and externals ordinal by FQN, edges by source then target, member edges by source then member
///     SymbolId, construction edges by source then constructed, injection edges by source then injected,
///     catch edges by source then caught, throw edges by source then thrown, exposure edges by source then
///     exposed, registrations by lifetime then
///     service then implementation) so serialization is stable; global ordering is re-derived at merge, so a
///     fragment's internal order never affects the model.
/// </remarks>
internal sealed record CodebaseFragment(
    string ProjectName,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<FragmentType> DeclaredTypes,
    IReadOnlyList<FragmentExternal> Externals,
    IReadOnlyList<FragmentEdge> Edges,
    IReadOnlyList<FragmentMemberEdge> MemberEdges,
    IReadOnlyList<FragmentConstructorEdge> ConstructorEdges,
    IReadOnlyList<FragmentInjectionEdge> InjectionEdges,
    IReadOnlyList<FragmentCatchEdge> CatchEdges,
    IReadOnlyList<FragmentThrowEdge> ThrowEdges,
    IReadOnlyList<FragmentExposureEdge> ExposureEdges,
    IReadOnlyList<FragmentServiceRegistration> ServiceRegistrations);
