namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     The extracted codebase: its types, the reference edges between them, and its projects — the
///     deterministic substrate the Phase 3 checker evaluates rules against. Every list is ordered
///     for reproducibility: <see cref="Types" /> by <see cref="TypeNode.FullName" /> (ordinal),
///     <see cref="Edges" /> by (source FullName, target FullName) (ordinal), and
///     <see cref="Projects" /> by <see cref="ProjectNode.Name" /> (ordinal).
/// </summary>
public sealed class CodebaseModel
{
    internal CodebaseModel(IReadOnlyList<TypeNode> types, IReadOnlyList<ReferenceEdge> edges, IReadOnlyList<ProjectNode> projects)
    {
        Types = types;
        Edges = edges;
        Projects = projects;
    }

    /// <summary>All types — solution-declared and shallow external nodes — ordered by FullName.</summary>
    public IReadOnlyList<TypeNode> Types { get; }

    /// <summary>All reference edges, ordered by (source FullName, target FullName).</summary>
    public IReadOnlyList<ReferenceEdge> Edges { get; }

    /// <summary>All projects, ordered by name.</summary>
    public IReadOnlyList<ProjectNode> Projects { get; }
}