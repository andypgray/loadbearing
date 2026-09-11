namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     The extracted codebase a check runs against: every type the solution declares, a shallow entry for
///     each external type they reference, the edges between them, the container registrations the source
///     spells, and the projects. Build one with the extractor in the <c>Zphil.LoadBearing.Roslyn</c>
///     package, then hand it to <c>ArchChecker.Check</c> beside an architecture model, or summarize it
///     with <see cref="GraphSummarizer" /> for the survey the CLI's <c>graph</c> verb prints. Every list
///     is read-only and ordered ordinal, so two runs over unchanged source produce the same lists in the
///     same order; each property below names the keys it is sorted by.
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
    ///     Gets every type in the model: the ones the solution's projects declare, and a shallow entry for each
    ///     external type they reference. Ordered by <see cref="TypeNode.FullName" />, declarations before external
    ///     entries, then by <see cref="TypeNode.ProjectName" />. A full name is not a key here: where a project
    ///     declares a name a referenced assembly also supplies, both entries are in this list, and
    ///     <see cref="ShadowedNames" /> names exactly those cases.
    /// </summary>
    // The (declaration-before-external, project) tie-break is what makes this a total order. FullName alone
    // is not one, because a shadowed name carries two nodes, and without the tie-break every rendered
    // document would take dictionary enumeration order and lose its byte-stability.
    public IReadOnlyList<TypeNode> Types { get; }

    /// <summary>
    ///     Gets the type-to-type reference edges: one entry per (source type, target type) pair, carrying every site
    ///     that produced it. Ordered by (source full name, target full name).
    /// </summary>
    public IReadOnlyList<ReferenceEdge> Edges { get; }

    /// <summary>
    ///     Gets the member uses: one entry per (source type, used member) pair — a property, field or event access, a
    ///     method call, a method-group reference — carrying every site. Ordered by (source full name, member
    ///     <see cref="MemberReference.SymbolId" />). Recorded beside <see cref="Edges" /> rather than instead of it, so
    ///     a member use also appears there as a reference edge to the member's containing type.
    /// </summary>
    public IReadOnlyList<MemberEdge> MemberEdges { get; }

    /// <summary>
    ///     Gets the object creations: one entry per (source type, constructed type) pair, carrying every site, from an
    ///     explicit <c>new Foo()</c> and a target-typed <c>new()</c> alike. Ordered by (source full name, constructed
    ///     full name). Recorded beside <see cref="Edges" /> rather than instead of it, so a creation also appears there
    ///     as a reference edge to the constructed type.
    /// </summary>
    public IReadOnlyList<ConstructorEdge> ConstructorEdges { get; }

    /// <summary>
    ///     Gets the constructor injections: one entry per (source type, injected parameter type) pair, read from the
    ///     declared instance constructors of each solution-declared type, primary constructors included. Ordered by
    ///     (source full name, injected full name). Recorded beside <see cref="Edges" /> rather than instead of it, so
    ///     an injected parameter type also appears there as a reference edge.
    /// </summary>
    public IReadOnlyList<InjectionEdge> InjectionEdges { get; }

    /// <summary>
    ///     Gets the caught exception types: one entry per (source type, caught type) pair, read from every <c>catch</c>
    ///     clause of each solution-declared type, where a bare <c>catch</c> records <c>System.Exception</c>. Ordered by
    ///     (source full name, caught full name). Each entry also carries <see cref="CatchEdge.UnfilteredSites" />, the
    ///     sites whose clause spells no <c>when</c> filter, and <see cref="CatchEdge.SwallowingSites" />, those of them
    ///     whose block does not end in a <c>throw</c>. A typed catch also appears in <see cref="Edges" />, its type
    ///     name being a reference like any other; a bare catch names no type and appears there not at all.
    /// </summary>
    public IReadOnlyList<CatchEdge> CatchEdges { get; }

    /// <summary>
    ///     Gets the thrown exception types: one entry per (source type, thrown type) pair, read from every <c>throw</c>
    ///     statement and throw expression of each solution-declared type and keyed on the thrown expression's static
    ///     type; a bare rethrow (<c>throw;</c>) records nothing. Ordered by (source full name, thrown full name). A
    ///     <c>throw new X()</c> appears here, in <see cref="ConstructorEdges" /> and in <see cref="Edges" /> alike.
    /// </summary>
    public IReadOnlyList<ThrowEdge> ThrowEdges { get; }

    /// <summary>
    ///     Gets the types named in public signature positions: one entry per (source type, exposed type) pair, read
    ///     from a method's return and parameter types and a property, field or event's type, for every member that is
    ///     public and whose containing types are public at every level. Ordered by (source full name, exposed full
    ///     name). Recorded beside <see cref="Edges" /> rather than instead of it, so a type the signature spells also
    ///     appears there as a reference edge.
    /// </summary>
    public IReadOnlyList<ExposureEdge> ExposureEdges { get; }

    /// <summary>
    ///     Gets the container registrations the source spells: one entry per (lifetime, service type, implementation
    ///     type) fact, ordered by those three. Held as fully-qualified names rather than on the type entries themselves
    ///     — <c>arch.Registered(lifetime)</c> selects the service and implementation names recorded at that lifetime,
    ///     resolved against this list when the check runs.
    /// </summary>
    public IReadOnlyList<ServiceRegistration> ServiceRegistrations { get; }

    /// <summary>
    ///     Gets every project in the model, ordered by name (ordinal).
    /// </summary>
    public IReadOnlyList<ProjectNode> Projects { get; }

    /// <summary>
    ///     Gets every full name a project declares that a referenced assembly also supplies, ordered by that name
    ///     (ordinal), and empty in the common case. This is the one place a name in <see cref="Types" /> does not
    ///     identify a single type, stated as a fact rather than left to be found by grouping that list;
    ///     <see cref="MergeNotes" /> reports the same split in prose.
    /// </summary>
    public IReadOnlyList<ShadowedName> ShadowedNames { get; }

    /// <summary>
    ///     Gets advisory notes about how this model was assembled, ready to show a reader: the project-level notes
    ///     first, each ordinal by project name, then the per-type notes, ordinal by fully-qualified name. Empty in the
    ///     common case. A note never means a failed load and never fails a check — the model is complete and correct,
    ///     and the note only warns that an attribution may surprise you. It is prose to display, not to parse:
    ///     <see cref="ShadowedNames" /> and <see cref="TypeNode.AlsoDeclaredBy" /> carry the same facts in a form to
    ///     act on.
    /// </summary>
    /// <remarks>
    ///     Three things get a note. A type that several projects declare, one note naming all of them: the
    ///     first declarer's compilation supplies the type's edges, members and hierarchy, while
    ///     <c>arch.Project</c> named on any declarer selects it. A project that compiles once per target
    ///     framework, one note per project naming its frameworks and the winning one: a type more than one of
    ///     them declares carries the first framework's facts alone, so whatever another framework's <c>#if</c>
    ///     guards is not in the model. A name a referenced assembly also supplies, one note per declaring
    ///     project: both types are in <see cref="Types" />, and each reference reaches whichever one the
    ///     referencing project bound, so a rule naming the type reaches both while an <c>arch.Project</c>
    ///     selection over the declaring project reaches the declaration alone.
    /// </remarks>
    public IReadOnlyList<string> MergeNotes { get; }
}
