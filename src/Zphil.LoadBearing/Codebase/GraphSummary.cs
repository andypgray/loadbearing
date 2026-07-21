namespace Zphil.LoadBearing.Codebase;

// The pre-spec codebase survey and its parts, clustered in one file: they are one cohesive
// summary shape produced together by GraphSummarizer, not independent product types. Sealed classes with
// internal constructors and get-only auto-props — the Codebase style (records are unavailable on Core's
// netstandard2.0 TFM; see Zphil.LoadBearing.csproj). Every list is ordinal-ordered for reproducibility.

/// <summary>
///     The extracted codebase, summarized for onboarding: its projects with their namespace inventories,
///     the observed cross-project reference edges, and the external references grouped by namespace root
///     — the deterministic pre-spec survey the derive flow orients on. Produced by
///     <see cref="GraphSummarizer" /> over a <see cref="CodebaseModel" />. Grouped counts only, never
///     per-site dumps (the minimal-token posture; sites arrive later from <c>check</c> on drafted rules).
/// </summary>
public sealed class GraphSummary
{
    internal GraphSummary(
        IReadOnlyList<ProjectSummary> projects,
        IReadOnlyList<ProjectEdgeSummary> projectEdges,
        IReadOnlyList<ExternalEdgeSummary> externalEdges)
    {
        Projects = projects;
        ProjectEdges = projectEdges;
        ExternalEdges = externalEdges;
    }

    /// <summary>The projects, ordered by name (ordinal) — the <see cref="CodebaseModel.Projects" /> order.</summary>
    public IReadOnlyList<ProjectSummary> Projects { get; }

    /// <summary>The observed cross-project reference edges, ordered by (source, target) (ordinal).</summary>
    public IReadOnlyList<ProjectEdgeSummary> ProjectEdges { get; }

    /// <summary>The external references grouped by namespace root, ordered by (source, root) (ordinal).</summary>
    public IReadOnlyList<ExternalEdgeSummary> ExternalEdges { get; }
}

/// <summary>
///     One project in the survey: its name, its declared forward project references (verbatim from the
///     <see cref="ProjectNode" />), the count of its solution-declared types, and its namespace inventory.
///     Comparing <see cref="ProjectReferences" /> against the <see cref="GraphSummary.ProjectEdges" />
///     surfaces declared-but-unobserved references (the dead-reference signal).
/// </summary>
public sealed class ProjectSummary
{
    internal ProjectSummary(string name, IReadOnlyList<string> projectReferences, int types, IReadOnlyList<NamespaceCount> namespaces)
    {
        Name = name;
        ProjectReferences = projectReferences;
        Types = types;
        Namespaces = namespaces;
    }

    /// <summary>The project (assembly) name.</summary>
    public string Name { get; }

    /// <summary>The names of the projects this project declares a reference to, ordinal-ordered.</summary>
    public IReadOnlyList<string> ProjectReferences { get; }

    /// <summary>The count of this project's solution-declared (non-external) types.</summary>
    public int Types { get; }

    /// <summary>The distinct namespaces of this project's declared types with per-namespace counts, ordinal by namespace.</summary>
    public IReadOnlyList<NamespaceCount> Namespaces { get; }
}

/// <summary>A namespace and the number of a project's solution-declared types that reside in it.</summary>
public sealed class NamespaceCount
{
    internal NamespaceCount(string @namespace, int types)
    {
        Namespace = @namespace;
        Types = types;
    }

    /// <summary>The namespace; the empty/global namespace renders as <c>(global)</c>.</summary>
    public string Namespace { get; }

    /// <summary>The count of the project's declared types in this namespace.</summary>
    public int Types { get; }
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