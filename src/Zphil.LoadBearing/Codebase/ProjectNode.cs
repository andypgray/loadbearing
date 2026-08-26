namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     A project in the extracted model: its name, whether the solution file declares it, its forward
///     project references (by name), the target frameworks it declares and — where one project file yielded
///     several compilations — the one whose facts its shared types carry, plus the artifact facts its build
///     evaluated: the packages it declares, whether it is packable, and whether its restore writes a lock
///     file. The reverse graph is derived in memory by consumers when needed; only the forward edges are
///     stored, ordinal-ordered for determinism.
/// </summary>
/// <remarks>
///     The artifact facts below are <em>evaluated</em>, never read out of the project file's XML: the
///     property a rule is about is very often set somewhere else (a <c>Directory.Build.props</c> above the
///     project) or nowhere at all (an SDK default), and only an evaluation knows which value won. Each is
///     therefore paired with the <c>file:line</c> that set it, and <see langword="null" /> means the
///     evaluation never happened rather than that the property is off — the tri-state
///     <see cref="SolutionMember" /> holds to.
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

    /// <summary>The project (assembly) name.</summary>
    public string Name { get; }

    /// <summary>The names of the projects this project references, ordinal-ordered.</summary>
    public IReadOnlyList<string> ProjectReferences { get; }

    /// <summary>
    ///     Whether the solution file declares this project, or <see langword="null" /> when membership was not
    ///     read. A workspace loads every project a <c>ProjectReference</c> reaches, which is a wider set than
    ///     the one the solution declares: <see langword="false" /> marks such a passenger, and it is a fact
    ///     about the solution rather than a defect in the project.
    /// </summary>
    /// <remarks>
    ///     <see langword="null" /> means unread, never "not a member" — an unreadable or unowned solution
    ///     format, a project whose file path the load never reported, or an extraction handed no membership
    ///     at all (every hand-built compilation). Consumers that filter on this must therefore treat unknown
    ///     as passing, or an unparsed solution would silently empty their view.
    /// </remarks>
    public bool? SolutionMember { get; }

    /// <summary>
    ///     Every target framework this project declares, ordinal-ordered and normalized to the short moniker
    ///     — so a classic project's <c>&lt;TargetFrameworkVersion&gt;v4.8&lt;/TargetFrameworkVersion&gt;</c>
    ///     reads <c>net48</c> beside an SDK project's own spelling. Empty only where there was nothing to
    ///     read it from, which is every hand-built input.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Ordinal order is also the order a workspace load hands a multi-targeted project's compilations
    ///         to extraction in, so on that path the first entry is the one a shared type's facts fall to.
    ///     </para>
    ///     <para>
    ///         Read from an evaluation where one ran, and otherwise from the frameworks the load itself
    ///         discriminated — which is why this can be populated while
    ///         <see cref="TargetFrameworksSite" /> is not. The fallback sees only what compiled, so it says
    ///         nothing about a framework a filtered run left out and nothing at all about a project that
    ///         compiled once.
    ///     </para>
    ///     <para>
    ///         A project with more than one entry is one <c>.csproj</c> that arrived as several Roslyn
    ///         projects. They union into this one node — its types, its references and its membership are the
    ///         union — and the union is lossless everywhere except <see cref="FactsFollow" />, which is the
    ///         one thing it cannot be.
    ///     </para>
    /// </remarks>
    public IReadOnlyList<string> TargetFrameworks { get; }

    /// <summary>
    ///     Where <see cref="TargetFrameworks" /> was declared, or <see langword="null" /> when nothing
    ///     evaluated this project. An evaluated project always carries a site even where it declares no
    ///     framework at all — its own file stands in — so a rule about what a project targets has somewhere
    ///     to point wherever it has something to judge.
    /// </summary>
    public SourceLocation? TargetFrameworksSite { get; }

    /// <summary>
    ///     The framework whose facts the types this project's frameworks <em>share</em> carry — the first
    ///     extracted — or <see langword="null" /> when nothing collapsed. A type each framework declares can
    ///     only carry one framework's edges, members and hierarchy, so a rule about it is checked against that
    ///     framework alone and whatever another framework's <c>#if</c> guards is not in the model at all.
    /// </summary>
    /// <remarks>
    ///     Null is deliberately not "the first of <see cref="TargetFrameworks" />": a project whose frameworks
    ///     share no type displaced nothing — every type keeps its own framework's facts — and naming a winner
    ///     there would be false about all of them. It is the same gate the merge's advisory note is under.
    /// </remarks>
    public string? FactsFollow { get; }

    /// <summary>
    ///     The packages this project <em>declares</em>, ordinal by name, each with the <c>file:line</c> that
    ///     declares it. Empty where nothing was evaluated, and empty for a project that declares none.
    /// </summary>
    /// <remarks>
    ///     Declared references only — the transitive package graph is a different fact, and one this model
    ///     does not hold. The list also excludes the references the SDK adds for a project implicitly, which
    ///     nobody wrote and nobody can remove.
    /// </remarks>
    public IReadOnlyList<PackageReference> PackageReferences { get; }

    /// <summary>
    ///     Whether this project produces a package, or <see langword="null" /> where there is no answer —
    ///     nothing evaluated it, or an evaluation that left <c>IsPackable</c> undefined, which is what a
    ///     project outside the SDK's pack machinery does.
    /// </summary>
    /// <remarks>
    ///     Almost always <see langword="true" /> without anybody having said so: the SDK defaults it on, so
    ///     the projects that ship and the projects that merely compile look alike until one of them opts out.
    ///     That is the whole reason this is evaluated rather than read from the project's own XML.
    /// </remarks>
    public bool? IsPackable { get; }

    /// <summary>
    ///     Where <see cref="IsPackable" /> was set, or <see langword="null" /> when it has no value. The
    ///     project's own file stands in wherever the winning declaration is not one this repository owns —
    ///     an SDK default has a real location, but it is a path on the machine that ran the build and says
    ///     nothing anybody can act on.
    /// </summary>
    public SourceLocation? IsPackableSite { get; }

    /// <summary>
    ///     Whether restoring this project writes a lock file (<c>RestorePackagesWithLockFile</c>), or
    ///     <see langword="null" /> where nothing evaluated it. Undeclared reads
    ///     <see langword="false" />: NuGet's default is off, so absence here is an answer rather than a gap
    ///     — which is what parts it from <see cref="IsPackable" />.
    /// </summary>
    public bool? LocksPackages { get; }

    /// <summary>
    ///     Where <see cref="LocksPackages" /> was set, or <see langword="null" /> when it has no value. This
    ///     is the fact most often declared away from the project — a solution-wide policy in a
    ///     <c>Directory.Build.props</c> — so the site is regularly a file the project itself never mentions.
    /// </summary>
    public SourceLocation? LocksPackagesSite { get; }
}

/// <summary>
///     One package a project declares: the package's name and the <c>file:line</c> of the declaration —
///     which may sit in a props file above the project rather than in the project itself.
/// </summary>
/// <remarks>
///     The version is deliberately absent. It is the fact that changes most often and matters least to a
///     structural rule, and holding it would put a model rebuild behind every dependency bump.
/// </remarks>
public sealed class PackageReference
{
    internal PackageReference(string name, SourceLocation site)
    {
        Name = name;
        Site = site;
    }

    /// <summary>The package identifier, verbatim.</summary>
    public string Name { get; }

    /// <summary>Where the reference is declared.</summary>
    public SourceLocation Site { get; }
}
