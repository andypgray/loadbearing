namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     The extracted codebase: its types, the reference edges between them, the member-use edges
///     (GRAMMAR §4.5), and its projects — the deterministic substrate the Phase 3 checker evaluates
///     rules against. Every list is ordered for reproducibility: <see cref="Types" /> by
///     <see cref="TypeNode.FullName" /> (ordinal), <see cref="Edges" /> by (source FullName, target
///     FullName) (ordinal), <see cref="MemberEdges" /> by (source FullName, member
///     <see cref="MemberReference.SymbolId" />) (ordinal), and <see cref="Projects" /> by
///     <see cref="ProjectNode.Name" /> (ordinal).
/// </summary>
public sealed class CodebaseModel
{
    internal CodebaseModel(
        IReadOnlyList<TypeNode> types,
        IReadOnlyList<ReferenceEdge> edges,
        IReadOnlyList<MemberEdge> memberEdges,
        IReadOnlyList<ProjectNode> projects)
    {
        Types = types;
        Edges = edges;
        MemberEdges = memberEdges;
        Projects = projects;
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

    /// <summary>All projects, ordered by name.</summary>
    public IReadOnlyList<ProjectNode> Projects { get; }
}