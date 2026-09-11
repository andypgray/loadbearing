namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     One project in the extracted codebase: its name, whether the solution file declares it, the
///     projects it references, the frameworks it targets, the packages it declares, and whether it packs
///     and locks its restore. These are the facts a rule about a project is judged on, so they are what
///     <c>MustOnlyTarget</c>, <c>MustReferenceNoPackages</c>, <c>MustLockPackages</c> and
///     <c>MustNotBePackable</c> read. Only forward project references are stored; build the reverse graph
///     yourself if you need it.
/// </summary>
/// <remarks>
///     The framework, package, packable and lock facts are evaluated rather than read out of the project
///     file's XML: the value that wins is very often set somewhere else, in a
///     <c>Directory.Build.props</c> above the project, or nowhere at all, as an SDK default, and only an
///     evaluation knows which. Each is paired with the <c>file:line</c> that set it, and
///     <see langword="null" /> means the evaluation never happened rather than that the property is off,
///     the same reading <see cref="SolutionMember" /> holds to. Every project verb passes on a fact that
///     is <see langword="null" />, so a load that could not evaluate a project reports gaps rather than
///     violations.
/// </remarks>
public sealed class ProjectNode : IProjectInfo
{
    internal ProjectNode(
        string name,
        IReadOnlyList<string> projectReferences,
        bool? solutionMember = null,
        IReadOnlyList<string>? targetFrameworks = null,
        SourceLocation? targetFrameworksSite = null,
        string? factsFollow = null,
        IReadOnlyList<PackageReference>? packageReferences = null,
        bool? isPackable = null,
        SourceLocation? isPackableSite = null,
        bool? locksPackages = null,
        SourceLocation? locksPackagesSite = null)
    {
        Name = name;
        ProjectReferences = projectReferences;
        SolutionMember = solutionMember;
        TargetFrameworks = targetFrameworks ?? [];
        TargetFrameworksSite = targetFrameworksSite;
        FactsFollow = factsFollow;
        PackageReferences = packageReferences ?? [];
        IsPackable = isPackable;
        IsPackableSite = isPackableSite;
        LocksPackages = locksPackages;
        LocksPackagesSite = locksPackagesSite;
    }

    /// <summary>
    ///     Gets the project (assembly) name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Gets this project's stable identity: <c>project:</c> followed by its <see cref="Name" />, displayed verbatim
    ///     wherever an identity is printed. It is what a baseline entry for a project rule is keyed by and what the
    ///     CLI's <c>--subject</c> filter matches, beside the type and member identities <c>TypeNode.SymbolId</c> and
    ///     <c>MemberNode.SymbolId</c>.
    /// </summary>
    // A project has no DocumentationCommentId to key on, so the tag is minted here rather than by
    // extraction. It wears a DocId's shape without ever colliding with one: every DocId tag is a single
    // letter, and the shared display helper strips only those, printing a longer tag verbatim. This is the
    // one owner of the literal — the stored form of an existing baseline entry depends on it, so it is not
    // free to move.
    public string SymbolId => "project:" + Name;

    /// <summary>
    ///     Gets the names of the projects this project declares a reference to, ordinal-ordered. Declared references
    ///     only, never the transitive closure.
    /// </summary>
    public IReadOnlyList<string> ProjectReferences { get; }

    /// <summary>
    ///     Gets whether the solution file declares this project, or <see langword="null" /> when membership was never
    ///     read: an unreadable or unrecognized solution format, a project whose file path the load never reported, or a
    ///     model built from compilations with no solution at all. A workspace loads every project a
    ///     <c>ProjectReference</c> reaches, which is a wider set than the solution declares, so
    ///     <see langword="false" /> marks a project that came along for the ride rather than a defect in it. Treat
    ///     <see langword="null" /> as passing when you filter on this, or an unread solution silently empties the view.
    /// </summary>
    public bool? SolutionMember { get; }

    /// <summary>
    ///     Gets every target framework this project declares, ordinal-ordered and normalized to the short moniker, so a
    ///     classic project's <c>&lt;TargetFrameworkVersion&gt;v4.8&lt;/TargetFrameworkVersion&gt;</c> reads
    ///     <c>net48</c> beside an SDK project's own spelling; <c>MustOnlyTarget</c> compares against these forms. Empty
    ///     only where there was nothing to read them from, which is any model built from compilations rather than from
    ///     a solution.
    /// </summary>
    /// <remarks>
    ///     Read from an evaluation where one ran, and otherwise from the frameworks the load itself told
    ///     apart, which is why this can be populated while <see cref="TargetFrameworksSite" /> is
    ///     <see langword="null" />. That fallback sees only what compiled, so it says nothing about a
    ///     framework a filtered run left out and nothing at all about a project that compiled once. More than
    ///     one entry means one project file that arrived as several compilations, ordered here as they were
    ///     extracted, so the first is the one <see cref="FactsFollow" /> names: the project's types, its
    ///     references and its membership are the union of them all, and that framework is the one thing the
    ///     union cannot express.
    /// </remarks>
    public IReadOnlyList<string> TargetFrameworks { get; }

    /// <summary>
    ///     Gets where <see cref="TargetFrameworks" /> was declared, or <see langword="null" /> when nothing evaluated
    ///     this project. An evaluated project always carries a site even where it declares no framework at all (its own
    ///     file stands in), so a rule about what a project targets has somewhere to point wherever it has something to
    ///     judge.
    /// </summary>
    public SourceLocation? TargetFrameworksSite { get; }

    /// <summary>
    ///     Gets the framework whose facts the types more than one of this project's frameworks declare carry, the first
    ///     one extracted, or <see langword="null" /> when no type collapsed — which includes every project that
    ///     compiles once. Such a type can carry only one framework's edges, members and hierarchy, so a rule about it
    ///     is checked against that framework alone and whatever another framework's <c>#if</c> guards is not in the
    ///     model at all. <see langword="null" /> is not shorthand for the first of <see cref="TargetFrameworks" />:
    ///     where the frameworks share no type, every type keeps its own framework's facts and no framework won.
    /// </summary>
    // Null here and the model's per-project multi-targeting merge note sit under one gate: both speak only
    // where a type actually collapsed, so naming a winner here would make the note's silence read as an
    // oversight.
    public string? FactsFollow { get; }

    /// <summary>
    ///     Gets the packages this project declares, ordinal by name, each with the <c>file:line</c> that declares it,
    ///     which may sit in a props file above the project. Declared references only: the transitive package graph is a
    ///     different fact and is not in the model, and the references the SDK adds implicitly, which nobody wrote and
    ///     nobody can remove, are left out. Empty where nothing was evaluated, and empty for a project that declares
    ///     none.
    /// </summary>
    public IReadOnlyList<PackageReference> PackageReferences { get; }

    /// <summary>
    ///     Gets whether this project produces a package, or <see langword="null" /> where there is no answer: nothing
    ///     evaluated it, or an evaluation left <c>IsPackable</c> undefined, which is what a project outside the SDK's
    ///     pack machinery does. Almost always <see langword="true" /> without anybody having said so, because the SDK
    ///     defaults it on, so the projects that ship and the projects that merely compile look alike until one of them
    ///     opts out.
    /// </summary>
    public bool? IsPackable { get; }

    /// <summary>
    ///     Gets where <see cref="IsPackable" /> was set, or <see langword="null" /> when it has no value. The project's
    ///     own file stands in wherever the winning declaration is not one the solution owns: an SDK default has a real
    ///     location, but it is a path on the machine that ran the build and says nothing anybody can act on.
    /// </summary>
    public SourceLocation? IsPackableSite { get; }

    /// <summary>
    ///     Gets whether restoring this project writes a lock file (<c>RestorePackagesWithLockFile</c>), or
    ///     <see langword="null" /> where nothing evaluated it. An evaluated project that declares nothing reads
    ///     <see langword="false" />, NuGet's default being off, so absence is an answer here rather than a gap, which
    ///     is what parts it from <see cref="IsPackable" />.
    /// </summary>
    public bool? LocksPackages { get; }

    /// <summary>
    ///     Gets where <see cref="LocksPackages" /> was set, or <see langword="null" /> when it has no value. This is
    ///     the fact most often declared away from the project, in a solution-wide <c>Directory.Build.props</c>, so the
    ///     site is regularly a file the project itself never mentions.
    /// </summary>
    public SourceLocation? LocksPackagesSite { get; }
}

/// <summary>
///     One package a project declares: the package's identifier and the <c>file:line</c> of the
///     declaration, which may sit in a props file above the project rather than in the project itself. The
///     version is not recorded.
/// </summary>
public sealed class PackageReference
{
    internal PackageReference(string name, SourceLocation site)
    {
        Name = name;
        Site = site;
    }

    /// <summary>
    ///     Gets the package identifier, verbatim.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Gets where the reference is declared.
    /// </summary>
    public SourceLocation Site { get; }
}
