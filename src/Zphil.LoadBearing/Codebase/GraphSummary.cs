namespace Zphil.LoadBearing.Codebase;

// The pre-spec codebase survey and its parts, clustered in one file: they are one cohesive
// summary shape produced together by GraphSummarizer, not independent product types. Sealed classes with
// internal constructors and get-only auto-props — the Codebase style (records are unavailable on Core's
// netstandard2.0 TFM; see Zphil.LoadBearing.csproj). Every list is ordinal-ordered for reproducibility.

/// <summary>
///     The extracted codebase, summarized for onboarding: its projects with their namespace inventories,
///     the observed cross-project reference edges, the external references grouped by namespace root, and
///     the types more than one project declares — the deterministic pre-spec survey the derive flow orients
///     on. Produced by <see cref="GraphSummarizer" /> over a <see cref="CodebaseModel" />. Grouped counts
///     only, never per-site dumps (the minimal-token posture; sites arrive later from <c>check</c> on
///     drafted rules).
/// </summary>
public sealed class GraphSummary
{
    internal GraphSummary(
        IReadOnlyList<ProjectSummary> projects,
        IReadOnlyList<ProjectEdgeSummary> projectEdges,
        IReadOnlyList<ExternalEdgeSummary> externalEdges,
        IReadOnlyList<MultiplyDeclaredTypeSummary> multiplyDeclaredTypes,
        IReadOnlyList<ShadowedTypeSummary> shadowedTypes)
    {
        Projects = projects;
        ProjectEdges = projectEdges;
        ExternalEdges = externalEdges;
        MultiplyDeclaredTypes = multiplyDeclaredTypes;
        ShadowedTypes = shadowedTypes;
    }

    /// <summary>The projects, ordered by name (ordinal) — the <see cref="CodebaseModel.Projects" /> order.</summary>
    public IReadOnlyList<ProjectSummary> Projects { get; }

    /// <summary>The observed cross-project reference edges, ordered by (source, target) (ordinal).</summary>
    public IReadOnlyList<ProjectEdgeSummary> ProjectEdges { get; }

    /// <summary>The external references grouped by namespace root, ordered by (source, root) (ordinal).</summary>
    public IReadOnlyList<ExternalEdgeSummary> ExternalEdges { get; }

    /// <summary>
    ///     The types more than one project declares, ordered by full name (ordinal), and empty for the
    ///     overwhelming common case — the survey's coverage statement about its own project attribution.
    /// </summary>
    public IReadOnlyList<MultiplyDeclaredTypeSummary> MultiplyDeclaredTypes { get; }

    /// <summary>
    ///     The full names a project declares that a referenced assembly also supplies, ordered by full name
    ///     (ordinal), and empty for the overwhelming common case — the survey's coverage statement about the
    ///     one place a name does not identify a type.
    /// </summary>
    public IReadOnlyList<ShadowedTypeSummary> ShadowedTypes { get; }
}

/// <summary>
///     One project in the survey: its name, whether the solution declares it, its declared forward project
///     references (verbatim from the <see cref="ProjectNode" />), the count of its solution-declared types,
///     how many of those a generator emitted, its namespace inventory, and — for a multi-targeted project —
///     the frameworks it was extracted from and the one its shared types' facts came from. Comparing
///     <see cref="ProjectReferences" /> against the <see cref="GraphSummary.ProjectEdges" /> surfaces
///     declared-but-unobserved references (the dead-reference signal).
/// </summary>
public sealed class ProjectSummary
{
    internal ProjectSummary(
        string name,
        IReadOnlyList<string> projectReferences,
        int types,
        int generated,
        IReadOnlyList<NamespaceCount> namespaces,
        bool? solutionMember = null,
        IReadOnlyList<string>? targetFrameworks = null,
        string? factsFollow = null)
    {
        Name = name;
        ProjectReferences = projectReferences;
        Types = types;
        Generated = generated;
        Namespaces = namespaces;
        SolutionMember = solutionMember;
        TargetFrameworks = targetFrameworks ?? [];
        FactsFollow = factsFollow;
    }

    /// <summary>The project (assembly) name.</summary>
    public string Name { get; }

    /// <summary>The names of the projects this project declares a reference to, ordinal-ordered.</summary>
    public IReadOnlyList<string> ProjectReferences { get; }

    /// <summary>The count of this project's solution-declared (non-external) types.</summary>
    public int Types { get; }

    /// <summary>
    ///     How many of <see cref="Types" /> a generator emitted (<see cref="ITypeInfo.IsGenerated" />) — a
    ///     subset of that count, never a separate population. It is the survey's answer to what a
    ///     project-anchored subject would sweep before a rule is written: a project whose two counts are
    ///     close is one where <c>arch.Project(…)</c> aims most of a rule at code nobody can fix, and
    ///     <c>.Authored()</c> is the narrowing that says so.
    /// </summary>
    public int Generated { get; }

    /// <summary>The distinct namespaces of this project's declared types with per-namespace counts, ordinal by namespace.</summary>
    public IReadOnlyList<NamespaceCount> Namespaces { get; }

    /// <summary>
    ///     <see cref="ProjectNode.SolutionMember" /> verbatim: whether the solution declares this project,
    ///     <see langword="false" /> for a passenger a reference edge dragged in, <see langword="null" /> when
    ///     membership was not read. The survey is where a passenger is meant to be investigated, so it is
    ///     reported here rather than filtered out.
    /// </summary>
    public bool? SolutionMember { get; }

    /// <summary>
    ///     <see cref="ProjectNode.TargetFrameworks" /> verbatim: every framework this project was extracted
    ///     from, ordinal-ordered, and empty for the single-framework project — which is every project of most
    ///     solutions, so most surveys say nothing here at all.
    /// </summary>
    public IReadOnlyList<string> TargetFrameworks { get; }

    /// <summary>
    ///     <see cref="ProjectNode.FactsFollow" /> verbatim: the framework whose facts this project's shared
    ///     types carry, or <see langword="null" /> when its frameworks share no type. It is the survey's
    ///     answer to what a rule anchored on this project is actually checked against — one compilation of
    ///     several, with every other framework's conditional code outside the model.
    /// </summary>
    public string? FactsFollow { get; }
}

/// <summary>A namespace and the number of a project's solution-declared types that reside in it.</summary>
public sealed class NamespaceCount
{
    internal NamespaceCount(string @namespace, int types, int generated)
    {
        Namespace = @namespace;
        Types = types;
        Generated = generated;
    }

    /// <summary>The namespace; the empty/global namespace renders as <c>(global)</c>.</summary>
    public string Namespace { get; }

    /// <summary>The count of the project's declared types in this namespace.</summary>
    public int Types { get; }

    /// <summary>
    ///     How many of <see cref="Types" /> a generator emitted — a subset of that count. A namespace where
    ///     the two are equal is wholly generator output, which is the shape that must never become a layer
    ///     glob: a compiled view tier collects under one namespace nobody typed.
    /// </summary>
    public int Generated { get; }
}

/// <summary>
///     An observed cross-project reference edge, grouped source-project → target-project.
///     <see cref="References" /> counts the distinct type-pairs (each <see cref="ReferenceEdge" /> is one
///     source-type → target-type pair). Same-project edges are deliberately excluded — the survey drives
///     cross-boundary rules, where a same-project reference is never a violation candidate.
/// </summary>
public sealed class ProjectEdgeSummary
{
    internal ProjectEdgeSummary(string source, string target, int references)
    {
        Source = source;
        Target = target;
        References = references;
    }

    /// <summary>The referencing project.</summary>
    public string Source { get; }

    /// <summary>The referenced project (never external).</summary>
    public string Target { get; }

    /// <summary>The number of distinct type-pairs observed from <see cref="Source" /> into <see cref="Target" />.</summary>
    public int References { get; }
}

/// <summary>
///     One type that several projects declare — a single source file compiled into more than one of them
///     (a linked <c>&lt;Compile Include&gt;</c>, shared source, a polyfill). <c>arch.Project</c> named on
///     any declarer selects it, but extraction attributes its facts to the first declarer — so this is what
///     a rule author needs <em>before</em> anchoring a subject on a project: whose compilation a rule over
///     this type answers from.
/// </summary>
public sealed class MultiplyDeclaredTypeSummary
{
    internal MultiplyDeclaredTypeSummary(string type, IReadOnlyList<string> declaredBy, string factsFollow)
    {
        Type = type;
        DeclaredBy = declaredBy;
        FactsFollow = factsFollow;
    }

    /// <summary>The type's fully-qualified name.</summary>
    public string Type { get; }

    /// <summary>
    ///     Every project that declares it, ordinal-ordered — <see cref="FactsFollow" /> among them, so the
    ///     entry reads as the whole roster rather than as the losers alone.
    /// </summary>
    public IReadOnlyList<string> DeclaredBy { get; }

    /// <summary>
    ///     The declarer whose facts and project attribution the type carries (the first declarer). Every
    ///     name in <see cref="DeclaredBy" /> selects it; this is the one whose compilation its facts
    ///     answer from.
    /// </summary>
    public string FactsFollow { get; }
}

/// <summary>
///     One full name that means two different types: a project declares it, and a referenced assembly no
///     project of this solution produces supplies it too — a stand-in declared under a package's own
///     namespace, or a polyfill under a BCL one. Both are in the model, and each reference reaches whichever
///     the referencing project actually bound, so this is what a rule author needs before writing a rule
///     about the name: an <c>arch.Project</c> selection over <see cref="DeclaredBy" /> reaches the declared
///     one alone, while a rule naming the type reaches both.
/// </summary>
public sealed class ShadowedTypeSummary
{
    internal ShadowedTypeSummary(
        string type, string declaredBy, IReadOnlyList<string> suppliedBy, IReadOnlyList<string> boundFromAssemblyBy)
    {
        Type = type;
        DeclaredBy = declaredBy;
        SuppliedBy = suppliedBy;
        BoundFromAssemblyBy = boundFromAssemblyBy;
    }

    /// <summary>The shared fully-qualified name.</summary>
    public string Type { get; }

    /// <summary>The project that declares it in source.</summary>
    public string DeclaredBy { get; }

    /// <summary>The referenced assemblies supplying the same name, ordinal-ordered.</summary>
    public IReadOnlyList<string> SuppliedBy { get; }

    /// <summary>
    ///     The projects whose references reach the assembly's type rather than the declaration,
    ///     ordinal-ordered — the half that makes the entry readable on its own, and the reason a scoped
    ///     survey keeps an entry whose declarer is out of scope.
    /// </summary>
    public IReadOnlyList<string> BoundFromAssemblyBy { get; }
}

/// <summary>
///     An external reference, grouped source-project → external namespace root (the first two dot-segments
///     of the target's namespace, e.g. <c>System.Data</c>). <see cref="References" /> counts the distinct
///     type-pairs into that root — the dangerous-external shortlist evidence, collapsed so a large BCL
///     surface reviews as a handful of roots instead of a per-type dump.
/// </summary>
public sealed class ExternalEdgeSummary
{
    internal ExternalEdgeSummary(string source, string targetNamespaceRoot, int references)
    {
        Source = source;
        TargetNamespaceRoot = targetNamespaceRoot;
        References = references;
    }

    /// <summary>The referencing project.</summary>
    public string Source { get; }

    /// <summary>
    ///     The first two dot-segments of the external target's namespace (one segment → that segment; empty →
    ///     <c>(global)</c>).
    /// </summary>
    public string TargetNamespaceRoot { get; }

    /// <summary>The number of distinct type-pairs observed from <see cref="Source" /> into this namespace root.</summary>
    public int References { get; }
}
