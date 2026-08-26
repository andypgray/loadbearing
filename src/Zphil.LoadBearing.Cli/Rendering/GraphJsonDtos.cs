namespace Zphil.LoadBearing.Cli.Rendering;

// The wire shape of `graph --json` — the pre-spec codebase survey, its own document with its own
// schemaVersion (1), distinct from check and status. Serialized camelCase, indented, nulls omitted.
// Grouped counts only, never per-site dumps (the minimal-token posture); sites come later from `check`.
// The optional slots below — grain, projectsScope, the coverage statements, and the workspace ones — are
// additive and null (omitted) on a full, unscoped survey of an all-C# solution whose workspace loaded,
// whose NuGet packages resolved and that no solution filter narrowed, so the schema stays version 1 and the
// default document is byte-identical to the one before they existed. A run whose model is incomplete
// reaches this document only under --allow-workspace-diagnostics, since graph otherwise refuses before
// extraction.
//
// multiplyDeclaredTypes and shadowedTypes are coverage statements about the survey's contents, so they
// elide to counts at skeleton grain; unsupportedProjects is one about its subject, so it rides the trust
// stamp instead and is elided at no grain.

/// <summary>The root <c>graph --json</c> document.</summary>
/// <param name="SchemaVersion">The survey document's schema version — 1.</param>
/// <param name="Solution">
///     The solution's file name — the survey's subject, and never a path, so the document is
///     machine-independent.
/// </param>
/// <param name="Grain">
///     <c>overview</c> when the namespace inventories were elided, <c>skeleton</c> when the
///     external-reference rows went with them, <c>index</c> when the survey is down to its project roster,
///     or null (omitted) at full grain — so a document that says nothing about grain is the complete one,
///     and a consumer can tell a coarser survey from a smaller codebase without diffing it.
/// </param>
/// <param name="ProjectsScope">
///     The project-name globs the survey was narrowed to, or null (omitted) when it covers the whole
///     solution. Present, it explains why <c>projectEdges</c> can name a project <c>projects</c> does not:
///     an edge survives scoping on either endpoint.
/// </param>
/// <param name="Projects">
///     One row per project the survey read, ordered by name. Present at every rung — it is the roster that
///     makes the floor a narrowing menu, and the argument <c>--projects</c> takes.
/// </param>
/// <param name="ProjectEdges">
///     The observed cross-project reference edges, grouped by (source, target) project pair, or null
///     (omitted) at index grain — the one thing that grain elides beyond skeleton's, and the last array here
///     that scales with the codebase. Null is "not rendered at this grain", never "none found";
///     <see cref="ProjectEdgeCount" /> tells the two apart.
/// </param>
/// <param name="ProjectEdgeCount">
///     How many observed edges the elision dropped, present only when <see cref="ProjectEdges" /> is elided.
///     Rendered on <see cref="ExternalEdgeCount" />'s rule and for its reason: an index survey still says
///     the projects reference each other, rather than reading like a solution of unrelated projects.
/// </param>
/// <param name="WorkspaceDiagnostics">
///     The workspace-load diagnostics, or null (omitted) when there were none — and at index grain, where
///     <see cref="WorkspaceDiagnosticCount" /> stands in. This is MSBuild's own words, one entry per project
///     per framework per complaint, so it is the one array here with no ceiling at all: measured on a
///     56-project bed whose NuGet audit feed was unreachable, it was 222,108 characters against a 4,703-character
///     roster. Its actionable half is already keyed — <see cref="FailedProjects" /> and
///     <see cref="RestoreFailedProjects" /> — which is what makes eliding the raw stream at the floor rung
///     safe rather than merely smaller.
/// </param>
/// <param name="WorkspaceDiagnosticCount">
///     How many diagnostics the elision dropped, present only when <see cref="WorkspaceDiagnostics" /> is
///     elided at index grain and there were some — never a bare <c>0</c>, so a clean load's survey is
///     untouched. Without it a survey that hit trouble and a survey that did not would read alike at the one
///     grain a reader reaches when nothing else fits.
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
///     needs before anchoring a subject on a project: <c>arch.Project</c> named on any declarer selects
///     the type, and its facts answer from <c>factsFollow</c>'s compilation.
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
/// <param name="UnsupportedProjects">
///     Which projects the solution declares that this product cannot read — each a solution-relative,
///     forward-slashed project path with the reason — or null (omitted) for an all-C# solution. The survey's
///     coverage statement about its own <em>subject</em> rather than its contents: without it the
///     <c>projects</c> array is simply shorter than the solution, and a reader who never opens the solution
///     file cannot tell a codebase with two projects from a survey that read two of three. Not scoped by
///     <c>projectsScope</c> — it is a fact about the load, like its three neighbours — and not elided at any
///     grain.
/// </param>
internal sealed record GraphJson(
    int SchemaVersion,
    string Solution,
    string? Grain,
    IReadOnlyList<string>? ProjectsScope,
    IReadOnlyList<GraphProjectJson> Projects,
    IReadOnlyList<GraphProjectEdgeJson>? ProjectEdges,
    int? ProjectEdgeCount,
    IReadOnlyList<GraphExternalEdgeJson>? ExternalEdges,
    int? ExternalEdgeCount,
    IReadOnlyList<GraphMultiplyDeclaredTypeJson>? MultiplyDeclaredTypes,
    int? MultiplyDeclaredTypeCount,
    IReadOnlyList<GraphShadowedTypeJson>? ShadowedTypes,
    int? ShadowedTypeCount,
    IReadOnlyList<string>? WorkspaceDiagnostics,
    int? WorkspaceDiagnosticCount,
    bool? ModelIncomplete,
    IReadOnlyList<string>? FailedProjects,
    IReadOnlyList<string>? UncheckedProjects,
    IReadOnlyList<string>? RestoreFailedProjects,
    IReadOnlyList<UnsupportedProjectStamp>? UnsupportedProjects);

/// <summary>
///     One project: whether the solution declares it, which target frameworks it was extracted from and
///     which one its shared types' facts came from, its declared references, solution-declared type count,
///     how many of those a generator emitted, and its namespace inventory — the last of which is null
///     (omitted) at overview grain, being the one thing that grain elides. At index grain the row is down to
///     what names and sizes the project: the frameworks pair and <c>projectReferences</c> go, leaving
///     <c>name</c>, <c>solutionMember</c>, <c>types</c> and <c>generated</c>.
/// </summary>
/// <remarks>
///     <para>
///         <c>solutionMember</c> follows the document-wide optional-field convention, and here it carries
///         real weight: absent means membership was never read, so a reader must not take a missing key for
///         <c>false</c>. An explicit <c>false</c> is a project the workspace loaded through a
///         <c>ProjectReference</c> that the solution file does not declare.
///     </para>
///     <para>
///         <c>targetFrameworks</c> and <c>factsFollow</c> ride the row for the reason <c>generated</c> does,
///         and are absent for the same reason: a single-framework project has one compilation, so its name
///         already says which one every fact came from and there is nothing to qualify. Where a project file
///         yielded several, the array names them all and <c>factsFollow</c> names the one the types they
///         share took their facts from — absent in its own right when the frameworks share no type, because
///         nothing was then displaced and claiming a winner would be false about every type in the project.
///         Riding the row is also what carries them down the grain ladder to its last rung: the pair scales
///         with the solution's projects, which every survey lists in full. They stop at index, where a row
///         answers only "which projects are there, and how big" and every qualifier on that answer is one
///         more multiple of the project count.
///     </para>
///     <para>
///         <c>projectReferences</c> is the row's one array and the reason index is a rung at all: what a
///         project declares scales with the solution's <em>edges</em> rather than its projects, so on a
///         large solution these lists are most of the roster's bulk. Elided at index it goes bare, with no
///         count — the same rule <c>namespaces</c> takes at overview: a row's own array carries its
///         document's grain stamp with it, so it needs no per-row restatement, while a document-level array
///         (<c>projectEdges</c>, <c>externalEdges</c>) elides to a count because nothing else on the
///         document says how much was there.
///     </para>
///     <para>
///         <c>generated</c> is a qualifier on the number it qualifies, in both places that number
///         appears — deliberately never a top-level coverage statement, because a generated type already
///         has a home in this project's <c>types</c> and in its namespace's.
///     </para>
/// </remarks>
internal sealed record GraphProjectJson(
    string Name,
    bool? SolutionMember,
    IReadOnlyList<string>? TargetFrameworks,
    string? FactsFollow,
    IReadOnlyList<string>? ProjectReferences,
    int Types,
    int? Generated,
    IReadOnlyList<GraphNamespaceJson>? Namespaces);

/// <summary>
///     A namespace and the count of the project's declared types in it, with how many of those a generator
///     emitted. <c>generated</c> is omitted at zero — most namespaces have none, and every survey of a
///     solution with no generators at all is byte-identical to the one before the key existed.
/// </summary>
internal sealed record GraphNamespaceJson(string Namespace, int Types, int? Generated);

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
