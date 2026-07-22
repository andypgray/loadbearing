namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     The extracted codebase: its types, the reference edges between them, the member-use edges
///     (GRAMMAR §4.5), the construction edges (§4.5), the constructor-injection edges (§4.7), the catch and
///     throw edges (§4.8), the signature-exposure edges (§4.9), the container-registration facts (§4.7), and
///     its projects — the deterministic substrate the checker evaluates rules against. Every list is ordered
///     for reproducibility:
///     <see cref="Types" /> by <see cref="TypeNode.FullName" /> (ordinal), <see cref="Edges" /> by (source
///     FullName, target FullName) (ordinal), <see cref="MemberEdges" /> by (source FullName, member
///     <see cref="MemberReference.SymbolId" />) (ordinal), <see cref="ConstructorEdges" /> by (source
///     FullName, constructed FullName) (ordinal), <see cref="InjectionEdges" /> by (source FullName,
///     injected FullName) (ordinal), <see cref="CatchEdges" /> by (source FullName, caught FullName)
///     (ordinal), <see cref="ThrowEdges" /> by (source FullName, thrown FullName) (ordinal),
///     <see cref="ExposureEdges" /> by (source FullName, exposed FullName) (ordinal),
///     <see cref="ServiceRegistrations" /> by (lifetime, service FullName,
///     implementation FullName) (ordinal), and <see cref="Projects" /> by
///     <see cref="ProjectNode.Name" /> (ordinal). <see cref="MergeNotes" /> carries the advisory
///     diagnostics the fragment merge raised while assembling this model.
/// </summary>
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
        MergeNotes = mergeNotes;
    }

    /// <summary>All types — solution-declared and shallow external nodes — ordered by FullName.</summary>
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
    ///     Advisory notes the fragment merge raised while assembling this model, ordinal-ordered so the
    ///     list is stable across runs. In v1 the sole source is same-FQN cross-project conflation: two
    ///     <em>differently named</em> projects declaring one fully-qualified type name, where the first
    ///     declarer wins the node's facts and <see cref="TypeNode.ProjectName" /> and the loser's copy is
    ///     therefore invisible to <c>arch.Project</c> selections. Purely informational — the model is
    ///     complete and correct, just ambiguous in its project attribution — so these never denote a
    ///     failed load and never gate <c>check</c> (unlike workspace-load diagnostics). Empty for the
    ///     overwhelming common case, including a project's own several target frameworks (same name,
    ///     silent).
    /// </summary>
    public IReadOnlyList<string> MergeNotes { get; }
}