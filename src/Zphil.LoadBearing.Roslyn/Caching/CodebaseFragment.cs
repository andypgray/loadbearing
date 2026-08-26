using Zphil.LoadBearing.Roslyn.Extraction;

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
///     <para>
///         A single fragment is one compilation's <em>narrow view</em>: a type another project declares still
///         appears in its <see cref="Externals" /> (as metadata), and the merge — not the fragment — decides
///         unification. The fragment's own collections are materialized in a canonical order (declared types
///         and externals ordinal by FQN, edges by source then target, member edges by source then member
///         SymbolId, construction edges by source then constructed, injection edges by source then injected,
///         catch edges by source then caught, throw edges by source then thrown, exposure edges by source then
///         exposed, registrations by lifetime then
///         service then implementation) so serialization is stable; global ordering is re-derived at merge, so a
///         fragment's internal order never affects the model.
///     </para>
///     <para>
///         <see cref="TargetFramework" /> sits beside <see cref="ProjectName" /> because it is the other half
///         of the same identity: where one project file yielded several compilations, the project name alone
///         no longer says which one a fact came from. It is <see langword="null" /> for the common
///         single-framework project, and the merge's framework-collapse note is gated on both sides being
///         known.
///     </para>
///     <para>
///         <see cref="SolutionMember" /> is the third identity fact and the one that is about the
///         <em>solution</em> rather than the compilation: whether the solution file declares this project, or
///         <see langword="null" /> where membership was never read. It trails the collections, and carries a
///         default, only so a hand-built fragment need not name a fact it has no way to know — the default is
///         the honest answer there, not a convenience.
///     </para>
///     <para>
///         <see cref="AssemblyName" /> is the fourth, and it is <em>not</em> <see cref="ProjectName" /> under
///         another name: the project name is the csproj's own file name, while this is what the compilation
///         emits, and an <c>&lt;AssemblyName&gt;</c> property or a renamed csproj parts them. The distinction
///         earns its place because this is the only string comparable against a
///         <see cref="FragmentExternal.AssemblyName" /> — the assembly some other fragment's compilation
///         actually bound a name to — which is how the merge tells a reference this project satisfied from one
///         a referenced assembly of the same full name satisfied. It carries a default for the same reason
///         <see cref="SolutionMember" /> does, and <see langword="null" /> is read as unknown throughout: an
///         unknown assembly never makes the merge conclude anything.
///     </para>
///     <para>
///         The trailing artifact facts are the fifth group, and the only ones that come from MSBuild rather
///         than from the compilation: what the project declares it targets, the packages it declares, whether
///         it packs, and whether its restore locks — each with the <c>file:line</c> that decided it, which is
///         regularly in a props file above the project. They carry defaults for
///         <see cref="SolutionMember" />'s reason and are read the same way: absent is unevaluated, never
///         off. <see cref="DeclaredTargetFrameworks" /> is <see cref="TargetFramework" />'s counterpart
///         rather than a longer spelling of it — this fragment is one framework's compilation, and that list
///         is every framework the project file names, which stays the same across the fragments of one
///         multi-targeted project.
///     </para>
/// </remarks>
internal sealed record CodebaseFragment(
    string ProjectName,
    string? TargetFramework,
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
    IReadOnlyList<FragmentServiceRegistration> ServiceRegistrations,
    bool? SolutionMember = null,
    string? AssemblyName = null,
    IReadOnlyList<string>? DeclaredTargetFrameworks = null,
    FragmentSite? TargetFrameworksSite = null,
    IReadOnlyList<FragmentPackageReference>? PackageReferences = null,
    bool? IsPackable = null,
    FragmentSite? IsPackableSite = null,
    bool? LocksPackages = null,
    FragmentSite? LocksPackagesSite = null);
