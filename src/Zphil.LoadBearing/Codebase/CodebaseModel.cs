namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     The extracted codebase — its types, the dependency edges between them, the container-registration
///     facts, and its projects: the deterministic substrate the checker evaluates rules against.
/// </summary>
/// <remarks>
///     Every list is ordered ordinal for reproducibility; each property states the sort key it uses.
/// </remarks>
public sealed class CodebaseModel
{
    internal CodebaseModel(
        IReadOnlyList<TypeNode> types,
        IReadOnlyList<ReferenceEdge> edges,
        IReadOnlyList<MemberEdge> memberEdges,
        IReadOnlyList<ConstructorEdge> constructorEdges,
        IReadOnlyList<InjectionEdge> injectionEdges,
        IReadOnlyList<CatchEdge> catchEdges,
        IReadOnlyList<ThrowEdge> throwEdges,
        IReadOnlyList<ExposureEdge> exposureEdges,
        IReadOnlyList<ServiceRegistration> serviceRegistrations,
        IReadOnlyList<ProjectNode> projects,
        IReadOnlyList<ShadowedName> shadowedNames,
        IReadOnlyList<string> mergeNotes)
    {
        Types = types;
        Edges = edges;
        MemberEdges = memberEdges;
        ConstructorEdges = constructorEdges;
        InjectionEdges = injectionEdges;
        CatchEdges = catchEdges;
        ThrowEdges = throwEdges;
        ExposureEdges = exposureEdges;
        ServiceRegistrations = serviceRegistrations;
        Projects = projects;
        ShadowedNames = shadowedNames;
        MergeNotes = mergeNotes;
    }

    /// <summary>
    ///     All types — solution-declared and shallow external nodes — ordered by FullName, then declarations
    ///     before externals, then by <see cref="TypeNode.ProjectName" />. The tie-break is load-bearing rather
    ///     than decorative: a name a project declares that a referenced assembly also supplies carries two
    ///     nodes, so FullName alone is not a total order and every rendered document would lose its
    ///     byte-stability to dictionary enumeration order without it.
    /// </summary>
    public IReadOnlyList<TypeNode> Types { get; }

    /// <summary>All reference edges, ordered by (source FullName, target FullName).</summary>
    public IReadOnlyList<ReferenceEdge> Edges { get; }

    /// <summary>
    ///     All member-use edges (GRAMMAR §4.5), ordered by (source FullName, member
    ///     <see cref="MemberReference.SymbolId" />). Recorded beside <see cref="Edges" />, never instead
    ///     of it: every member use also mints a type-level edge to the member's containing type.
    /// </summary>
    public IReadOnlyList<MemberEdge> MemberEdges { get; }

    /// <summary>
    ///     All construction edges (GRAMMAR §4.5), ordered by (source FullName, constructed FullName).
    ///     Recorded beside <see cref="Edges" />, never instead of it: every <c>new Foo()</c> also mints a
    ///     type-level edge to the constructed type.
    /// </summary>
    public IReadOnlyList<ConstructorEdge> ConstructorEdges { get; }

    /// <summary>
    ///     All constructor-injection edges (GRAMMAR §4.7), ordered by (source FullName, injected FullName).
    ///     Read from the declared instance constructors of each solution-declared type (primary constructors
    ///     included). Recorded beside <see cref="Edges" />, never instead of it: an injected parameter type
    ///     also mints a type-level edge to that type.
    /// </summary>
    public IReadOnlyList<InjectionEdge> InjectionEdges { get; }

    /// <summary>
    ///     All catch edges (GRAMMAR §4.8), ordered by (source FullName, caught FullName). Read from every
    ///     <c>catch</c> clause of each solution-declared type; a bare <c>catch</c> records
    ///     <c>System.Exception</c>. A typed catch is recorded beside <see cref="Edges" />, never instead of
    ///     it: its type-name syntax also mints a type-level edge (a bare catch names no type, so mints none).
    ///     Each edge additionally carries its <see cref="CatchEdge.UnfilteredSites" /> — the subset of its
    ///     sites whose clause spells no <c>when</c> filter — and its <see cref="CatchEdge.SwallowingSites" />,
    ///     the subset of <em>those</em> whose block does not end in a <c>throw</c>.
    /// </summary>
    public IReadOnlyList<CatchEdge> CatchEdges { get; }

    /// <summary>
    ///     All throw edges (GRAMMAR §4.8), ordered by (source FullName, thrown FullName). Read from every
    ///     <c>throw</c> statement and throw expression of each solution-declared type, keyed on the thrown
    ///     expression's static type; a bare rethrow (<c>throw;</c>) records nothing. A <c>throw new X()</c> is
    ///     recorded beside its <see cref="ConstructorEdges">construction edge</see> and the type-level edge.
    /// </summary>
    public IReadOnlyList<ThrowEdge> ThrowEdges { get; }

    /// <summary>
    ///     All signature-exposure edges (GRAMMAR §4.9), ordered by (source FullName, exposed FullName). Read
    ///     from every public signature position — a method's return and parameter types, a property/field/event
    ///     type — of each effectively-public member (a public member whose containing-type chain is public at
    ///     every level) of each solution-declared type. Recorded beside <see cref="Edges" />, never instead of
    ///     it: the signature type-name syntax also mints a type-level edge to that type.
    /// </summary>
    public IReadOnlyList<ExposureEdge> ExposureEdges { get; }

    /// <summary>
    ///     All container-registration facts (GRAMMAR §4.7), ordered by (lifetime, service FullName,
    ///     implementation FullName). Held as FQN strings (never denormalized onto <see cref="TypeNode" />):
    ///     <c>arch.Registered(lifetime)</c> membership is the union of service and implementation FQNs at
    ///     that lifetime, resolved at evaluation against these facts.
    /// </summary>
    public IReadOnlyList<ServiceRegistration> ServiceRegistrations { get; }

    /// <summary>All projects, ordered by name.</summary>
    public IReadOnlyList<ProjectNode> Projects { get; }

    /// <summary>
    ///     Every full name a project declares that a referenced assembly also supplies, ordered by that name
    ///     (ordinal), and empty for the overwhelming common case — the one place a name in
    ///     <see cref="Types" /> does not identify a type, stated as a fact rather than left to be rediscovered
    ///     by grouping that list. The prose form is the third <see cref="MergeNotes">merge note</see> kind.
    /// </summary>
    public IReadOnlyList<ShadowedName> ShadowedNames { get; }

    /// <summary>
    ///     Advisory notes the fragment merge raised while assembling this model: the two project-level kinds
    ///     first, each ordinal by project name, then the per-type kind, ordinal by fully-qualified name — so
    ///     the list is stable across runs and the coarser fact is read first.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Per-type — same-FQN cross-project conflation.</b> Two or more <em>differently named</em>
    ///         projects declare one fully-qualified type name; the first declarer wins the node's facts and
    ///         <see cref="TypeNode.ProjectName" />, while <c>arch.Project</c> selections reach every
    ///         declarer and a declarer's reference to its own compiled-in copy counts against that declarer
    ///         alone. One note per conflated type names all of them, so a type several projects shadow
    ///         costs one line. The same fact rides the node itself as
    ///         <see cref="TypeNode.AlsoDeclaredBy" />, for a consumer that must act on it rather than
    ///         report it.
    ///     </para>
    ///     <para>
    ///         <b>Per-project — a multi-target-framework collapse.</b> One project file's several target
    ///         frameworks share a name, so they union into one project; where two of them declare the same
    ///         type, that type can only carry one framework's facts (the first extracted), and a rule about
    ///         it is therefore checked against that framework alone. One note per project, naming every
    ///         framework it targets and the winning one. A project whose frameworks share <em>no</em> type —
    ///         and a framework-exclusive type, which keeps its own framework's facts — stays silent, because
    ///         nothing collapsed.
    ///     </para>
    ///     <para>
    ///         <b>Per-project — a name a referenced assembly also supplies.</b> A project declares a
    ///         fully-qualified name that an assembly no project of this solution produces supplies too — a
    ///         stand-in under a package's own namespace, a polyfill under a BCL one. Both types are in
    ///         <see cref="Types" />, and each reference reaches whichever the referencing compilation bound,
    ///         so a rule naming the type reaches both while an <c>arch.Project</c> selection over the
    ///         declaring project reaches only the declaration. One note per declaring project rather than per
    ///         name, on the same reasoning as the framework note above: the answer is the same for all of
    ///         them, and a solution carrying a dozen shims would otherwise spend a dozen lines. The queryable
    ///         form is the second node itself, and — for a consumer that needs the split named rather than
    ///         inferred from two nodes wearing one name — <see cref="ShadowedNames" />, which is also what
    ///         the survey's coverage key is a projection of.
    ///     </para>
    ///     <para>
    ///         All three kinds are purely informational — the model is complete and correct, just carrying an
    ///         attribution a reader can be surprised by — so they never denote a failed load and never gate
    ///         <c>check</c> (unlike workspace-load diagnostics). Empty for the overwhelming common case.
    ///     </para>
    /// </remarks>
    public IReadOnlyList<string> MergeNotes { get; }
}
