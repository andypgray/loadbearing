namespace Zphil.LoadBearing.Codebase;

// The pre-spec codebase survey and its parts, clustered in one file: they are one cohesive
// summary shape produced together by GraphSummarizer, not independent product types. Sealed classes with
// internal constructors and get-only auto-props — the Codebase style (records are unavailable on Core's
// netstandard2.0 TFM; see Zphil.LoadBearing.csproj). Every list is ordinal-ordered for reproducibility.

/// <summary>
///     A survey of an extracted codebase, needing no architecture spec: the projects with their namespace
///     inventories, the reference edges observed between them, the external references grouped by
///     namespace root, and the names more than one place declares. Produced by
///     <see cref="GraphSummarizer" /> over a <see cref="CodebaseModel" />, and what the CLI's <c>graph</c>
///     verb prints and <c>graph --json</c> serializes — what to read when orienting on an unfamiliar
///     solution or working out which rules are worth writing. Grouped counts only: no list of sites
///     appears here, and the sites arrive from <c>check</c> once a rule names them.
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

    /// <summary>
    ///     Gets the surveyed projects, ordered by name (ordinal).
    /// </summary>
    public IReadOnlyList<ProjectSummary> Projects { get; }

    /// <summary>
    ///     Gets the reference edges observed between projects, ordered by (source, target) (ordinal). These are the
    ///     references the code actually makes, which is not the same list as the references the projects declare.
    /// </summary>
    public IReadOnlyList<ProjectEdgeSummary> ProjectEdges { get; }

    /// <summary>
    ///     Gets the references out to types no project of the solution declares, grouped by namespace root and ordered
    ///     by (source, root) (ordinal).
    /// </summary>
    public IReadOnlyList<ExternalEdgeSummary> ExternalEdges { get; }

    /// <summary>
    ///     Gets the types more than one project declares, ordered by full name (ordinal). Empty in the common case, and
    ///     reading it is how you know whether the per-project figures above can be taken at face value.
    /// </summary>
    public IReadOnlyList<MultiplyDeclaredTypeSummary> MultiplyDeclaredTypes { get; }

    /// <summary>
    ///     Gets the full names a project declares that a referenced assembly also supplies, ordered by full name
    ///     (ordinal). Empty in the common case, and the one place in the survey where a name does not identify a single
    ///     type.
    /// </summary>
    public IReadOnlyList<ShadowedTypeSummary> ShadowedTypes { get; }
}

/// <summary>
///     One project in the survey: its name, whether the solution declares it, the project references it
///     declares, how many types it declares and how many of those a generator emitted, its namespace
///     inventory, the packages it declares, whether it packs and locks its restore, and — for a project
///     that compiles once per framework — the frameworks it was extracted from and the one its shared
///     types' facts came from. Comparing <see cref="ProjectReferences" /> against
///     <see cref="GraphSummary.ProjectEdges" /> is how a reference that is declared but never used shows
///     up.
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
        string? factsFollow = null,
        IReadOnlyList<string>? packageReferences = null,
        bool? isPackable = null,
        bool? locksPackages = null)
    {
        Name = name;
        ProjectReferences = projectReferences;
        Types = types;
        Generated = generated;
        Namespaces = namespaces;
        SolutionMember = solutionMember;
        TargetFrameworks = targetFrameworks ?? [];
        FactsFollow = factsFollow;
        PackageReferences = packageReferences ?? [];
        IsPackable = isPackable;
        LocksPackages = locksPackages;
    }

    /// <summary>
    ///     Gets the project (assembly) name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Gets the names of the projects this project declares a reference to, ordinal-ordered.
    /// </summary>
    public IReadOnlyList<string> ProjectReferences { get; }

    /// <summary>
    ///     Gets the count of this project's solution-declared (non-external) types.
    /// </summary>
    public int Types { get; }

    /// <summary>
    ///     Gets how many of <see cref="Types" /> a generator emitted (<see cref="ITypeInfo.IsGenerated" />), a subset
    ///     of that count, never a separate population. A project whose two counts are close is one where
    ///     <c>arch.Project(…)</c> would aim most of a rule at code nobody can edit, and <c>Authored()</c> is the
    ///     narrowing that leaves it out.
    /// </summary>
    public int Generated { get; }

    /// <summary>
    ///     Gets the distinct namespaces of this project's declared types with a count for each, ordinal by namespace.
    /// </summary>
    public IReadOnlyList<NamespaceCount> Namespaces { get; }

    /// <summary>
    ///     Gets whether the solution declares this project, <see langword="false" /> for one a reference edge dragged
    ///     in rather than the solution naming it, or <see langword="null" /> when membership was not read. Such a
    ///     passenger is reported rather than filtered out: the survey is where it is meant to be noticed.
    /// </summary>
    public bool? SolutionMember { get; }

    /// <summary>
    ///     Gets every framework this project declares, ordinal-ordered and normalized to the short moniker
    ///     (<c>net48</c>, <c>net8.0</c>). Empty only where nothing was evaluated to answer with.
    /// </summary>
    public IReadOnlyList<string> TargetFrameworks { get; }

    /// <summary>
    ///     Gets the framework whose facts this project's shared types carry, or <see langword="null" /> when its
    ///     frameworks collapsed no type. It says what a rule about this project is actually checked against: one
    ///     compilation of several, with every other framework's conditional code outside the model.
    /// </summary>
    public string? FactsFollow { get; }

    /// <summary>
    ///     Gets the names of the packages this project declares, ordinal-ordered. Names alone, the survey being grouped
    ///     counts throughout; <see cref="ProjectNode.PackageReferences" /> carries the <c>file:line</c> each was
    ///     declared at.
    /// </summary>
    public IReadOnlyList<string> PackageReferences { get; }

    /// <summary>
    ///     Gets whether the project produces a package, or <see langword="null" /> where there is no answer. This is
    ///     where the handful of projects a solution actually ships are told apart from the many that merely compile.
    /// </summary>
    public bool? IsPackable { get; }

    /// <summary>
    ///     Gets whether restoring this project writes a lock file, or <see langword="null" /> where nothing evaluated
    ///     it.
    /// </summary>
    public bool? LocksPackages { get; }
}

/// <summary>A namespace and how many of a project's declared types reside in it.</summary>
public sealed class NamespaceCount
{
    internal NamespaceCount(string @namespace, int types, int generated)
    {
        Namespace = @namespace;
        Types = types;
        Generated = generated;
    }

    /// <summary>
    ///     Gets the namespace; the global namespace renders as <c>(global)</c>.
    /// </summary>
    public string Namespace { get; }

    /// <summary>
    ///     Gets the count of the project's declared types in this namespace.
    /// </summary>
    public int Types { get; }

    /// <summary>
    ///     Gets how many of <see cref="Types" /> a generator emitted — a subset of that count. A namespace where the
    ///     two are equal is wholly generator output, which is the shape that must never become a layer glob: a compiled
    ///     view tier collects under one namespace nobody typed.
    /// </summary>
    public int Generated { get; }
}

/// <summary>
///     A reference edge observed from one project into another, with the number of distinct type pairs
///     behind it. References within a single project are left out; the survey is about the crossings
///     between projects, which is what a layering or boundary rule is written about.
/// </summary>
public sealed class ProjectEdgeSummary
{
    internal ProjectEdgeSummary(string source, string target, int references)
    {
        Source = source;
        Target = target;
        References = references;
    }

    /// <summary>
    ///     Gets the referencing project.
    /// </summary>
    public string Source { get; }

    /// <summary>
    ///     Gets the referenced project (never external).
    /// </summary>
    public string Target { get; }

    /// <summary>
    ///     Gets the number of distinct type-pairs observed from <see cref="Source" /> into <see cref="Target" />.
    /// </summary>
    public int References { get; }
}

/// <summary>
///     One type that several projects declare — a single source file compiled into more than one of them:
///     a linked <c>&lt;Compile Include&gt;</c>, shared source, a polyfill. <c>arch.Project</c> named on
///     any declarer selects it, but its facts come from the first declarer alone, so this is what to read
///     before writing a rule about a project: whose compilation a rule over this type answers from.
/// </summary>
public sealed class MultiplyDeclaredTypeSummary
{
    internal MultiplyDeclaredTypeSummary(string type, IReadOnlyList<string> declaredBy, string factsFollow)
    {
        Type = type;
        DeclaredBy = declaredBy;
        FactsFollow = factsFollow;
    }

    /// <summary>
    ///     Gets the type's fully-qualified name.
    /// </summary>
    public string Type { get; }

    /// <summary>
    ///     Gets every project that declares it, ordinal-ordered, <see cref="FactsFollow" /> among them: the whole
    ///     roster, not only the declarers whose facts were displaced.
    /// </summary>
    public IReadOnlyList<string> DeclaredBy { get; }

    /// <summary>
    ///     Gets the declarer whose facts and project attribution the type carries: the first one. Every name in
    ///     <see cref="DeclaredBy" /> selects the type, but this is the project whose compilation its edges, members and
    ///     hierarchy answer from.
    /// </summary>
    public string FactsFollow { get; }
}

/// <summary>
///     One full name that means two different types: a project declares it, and an assembly no project of
///     this solution produces supplies it too — a stand-in written under a package's own namespace, a
///     polyfill under a framework one. Both are in the model, and each reference reaches whichever one the
///     referencing project actually bound, so read this before writing a rule about the name: an
///     <c>arch.Project</c> selection over <see cref="DeclaredBy" /> reaches the declared type alone, while
///     a rule naming the type reaches both.
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

    /// <summary>
    ///     Gets the shared fully-qualified name.
    /// </summary>
    public string Type { get; }

    /// <summary>
    ///     Gets the project that declares it in source.
    /// </summary>
    public string DeclaredBy { get; }

    /// <summary>
    ///     Gets the referenced assemblies supplying the same name, ordinal-ordered.
    /// </summary>
    public IReadOnlyList<string> SuppliedBy { get; }

    /// <summary>
    ///     Gets the projects whose references reach the assembly's type rather than the declaration, ordinal-ordered:
    ///     the half that says whom the split costs, and the reason a survey narrowed to some projects keeps an entry
    ///     whose declarer is outside them.
    /// </summary>
    public IReadOnlyList<string> BoundFromAssemblyBy { get; }
}

/// <summary>
///     The references from one project out to types under one external namespace root — the first two
///     dot-segments of the target's namespace, such as <c>System.Data</c> — with the number of distinct
///     type pairs behind them. Grouping this way is what lets a large framework surface review as a
///     handful of roots instead of a per-type list.
/// </summary>
public sealed class ExternalEdgeSummary
{
    internal ExternalEdgeSummary(string source, string targetNamespaceRoot, int references)
    {
        Source = source;
        TargetNamespaceRoot = targetNamespaceRoot;
        References = references;
    }

    /// <summary>
    ///     Gets the referencing project.
    /// </summary>
    public string Source { get; }

    /// <summary>
    ///     Gets the first two dot-segments of the external target's namespace; a one-segment namespace gives that
    ///     segment, and the global namespace gives <c>(global)</c>.
    /// </summary>
    public string TargetNamespaceRoot { get; }

    /// <summary>
    ///     Gets the number of distinct type-pairs observed from <see cref="Source" /> into this namespace root.
    /// </summary>
    public int References { get; }
}
