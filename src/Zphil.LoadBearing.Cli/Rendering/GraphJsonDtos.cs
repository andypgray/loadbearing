namespace Zphil.LoadBearing.Cli.Rendering;

// The wire shape of `graph --json` — the pre-spec codebase survey, its own document with its own
// schemaVersion (1), distinct from check and status. Serialized camelCase, indented, nulls omitted.
// Grouped counts only, never per-site dumps (the minimal-token posture); sites come later from `check`.
// The eight optional slots below — grain, projectsScope, multiplyDeclaredTypes, and the five workspace ones
// — are additive and null (omitted) on a full, unscoped survey of a healthy solution whose workspace
// loaded, whose NuGet packages resolved and that no solution filter narrowed, so the schema stays version 1
// and the default document is byte-identical to the one before they existed. A run whose model is
// incomplete reaches this document only under --allow-workspace-diagnostics, since graph otherwise refuses
// before extraction.
//
// multiplyDeclaredTypes is the survey's first COVERAGE STATEMENT — a flat, optional, top-level key saying
// what the survey above does not cover, absent when there is nothing to say, and elided to a count at
// skeleton grain because its content scales with the codebase rather than with the schema.

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
///     <see langword="true" /> when a project failed to load or a project's NuGet packages are not in the
///     model, so the survey below covers only what the model actually holds — projects, types, and edges are
///     all missing, not merely fewer; null (omitted) otherwise.
/// </param>
/// <param name="FailedProjects">
///     Which projects are missing from the survey — solution-relative, forward-slashed <c>.csproj</c> paths
///     — or null (omitted) when none are. On a survey this is the most useful slot of the four: it names
///     precisely what a reader would otherwise have to notice was absent.
/// </param>
/// <param name="RestoreFailedProjects">
///     Which projects' NuGet packages are not in the model — solution-relative, forward-slashed
///     <c>.csproj</c> paths — or null (omitted) when none are. The slot a survey needs most sharply of the
///     four, because this is the document where the damage is directly visible and still deniable: these
///     projects are present with all their types, so nothing looks absent — but <see cref="ExternalEdges" />
///     is missing precisely the rows their package references would have produced, and a short external-edge
///     list reads as a codebase with few dependencies.
/// </param>
/// <param name="UncheckedProjects">
///     Which projects the solution declares that this survey never loaded — solution-relative,
///     forward-slashed <c>.csproj</c> paths — or null (omitted) when it covers the whole solution.
///     Non-empty only under a <c>.slnf</c> solution filter, and the survey's counterpart to
///     <see cref="FailedProjects" /> for a universe that is smaller rather than wrong: a project or edge
///     absent from a survey carrying this slot may simply be out of view. Measured as what the solution
///     declares minus what loaded, so a filter that narrows nothing omits the key.
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
/// <param name="MultiplyDeclaredTypes">
///     The types more than one project declares — one source file compiled into several of them — each with
///     every declaring project and the one whose facts and project attribution the type follows. Null
///     (omitted) when the solution has none, which is the overwhelming common case, and null at skeleton
///     grain too, where <see cref="MultiplyDeclaredTypeCount" /> stands in for it. The fact a rule author
///     needs before anchoring a subject on a project: <c>arch.Project</c> named on any declarer but
///     <c>factsFollow</c> will not select the type.
/// </param>
/// <param name="MultiplyDeclaredTypeCount">
///     How many multiply-declared entries the elision dropped, present only when
///     <see cref="MultiplyDeclaredTypes" /> is elided at skeleton grain — never rendered as a bare
///     <c>0</c>, so a healthy solution's document is untouched.
/// </param>
/// <param name="ShadowedTypes">
///     The full names a project declares that a referenced assembly also supplies — a stand-in under a
///     package's namespace, a polyfill under a BCL one — each with the declaring project, the supplying
///     assemblies, and the projects whose references reach the assembly's type instead. Null (omitted) when
///     the solution has none, and null at skeleton grain where <see cref="ShadowedTypeCount" /> stands in.
///     The one place a name in this model does not identify a type: a rule naming it reaches both, while
///     <c>arch.Project</c> over <c>declaredBy</c> reaches the declared one alone.
/// </param>
/// <param name="ShadowedTypeCount">
///     How many shadowed-name entries the elision dropped, present only when <see cref="ShadowedTypes" /> is
///     elided at skeleton grain — never a bare <c>0</c>, on the same rule as its sibling above.
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
    IReadOnlyList<GraphMultiplyDeclaredTypeJson>? MultiplyDeclaredTypes,
    int? MultiplyDeclaredTypeCount,
    IReadOnlyList<GraphShadowedTypeJson>? ShadowedTypes,
    int? ShadowedTypeCount,
    IReadOnlyList<string>? WorkspaceDiagnostics,
    bool? ModelIncomplete,
    IReadOnlyList<string>? FailedProjects,
    IReadOnlyList<string>? UncheckedProjects,
    IReadOnlyList<string>? RestoreFailedProjects);

/// <summary>
///     One project: whether the solution declares it, its declared references, solution-declared type count,
///     and namespace inventory — the last of which is null (omitted) at overview grain, being the one thing
///     that grain elides.
/// </summary>
/// <remarks>
///     <c>solutionMember</c> follows the document-wide optional-field convention, and here it carries real
///     weight: absent means membership was never read, so a reader must not take a missing key for
///     <c>false</c>. An explicit <c>false</c> is a project the workspace loaded through a
///     <c>ProjectReference</c> that the solution file does not declare.
/// </remarks>
internal sealed record GraphProjectJson(
    string Name,
    bool? SolutionMember,
    IReadOnlyList<string> ProjectReferences,
    int Types,
    IReadOnlyList<GraphNamespaceJson>? Namespaces);

/// <summary>A namespace and the count of the project's declared types in it.</summary>
internal sealed record GraphNamespaceJson(string Namespace, int Types);

/// <summary>An observed cross-project reference edge with its distinct type-pair count.</summary>
internal sealed record GraphProjectEdgeJson(string Source, string Target, int References);

/// <summary>An external reference grouped by target namespace root, with its distinct type-pair count.</summary>
internal sealed record GraphExternalEdgeJson(string Source, string TargetNamespaceRoot, int References);

/// <summary>
///     One type several projects declare: its full name, every declaring project, and the declarer whose
///     facts won. <c>declaredBy</c> carries the winner too, so the entry reads whole rather than as a losers
///     list a reader has to add <c>factsFollow</c> back into.
/// </summary>
internal sealed record GraphMultiplyDeclaredTypeJson(
    string Type,
    IReadOnlyList<string> DeclaredBy,
    string FactsFollow);

/// <summary>
///     One full name that denotes two types: the project declaring it, the referenced assemblies supplying
///     it, and the projects whose references reach the assembly's rather than the declaration.
///     <c>boundFromAssemblyBy</c> is what makes the entry readable on its own — without it a reader knows a
///     name is shared but not whether anything actually depends on the split.
/// </summary>
internal sealed record GraphShadowedTypeJson(
    string Type,
    string DeclaredBy,
    IReadOnlyList<string> SuppliedBy,
    IReadOnlyList<string> BoundFromAssemblyBy);
