namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     A project in the extracted model: its name and its forward project references (by name).
///     The reverse graph is derived in memory by consumers when needed; only the forward
///     edges are stored, ordinal-ordered for determinism.
/// </summary>
public sealed class ProjectNode
{
    internal ProjectNode(string name, IReadOnlyList<string> projectReferences)
    {
        Name = name;
        ProjectReferences = projectReferences;
    }

    /// <summary>The project (assembly) name.</summary>
    public string Name { get; }

    /// <summary>The names of the projects this project references, ordinal-ordered.</summary>
    public IReadOnlyList<string> ProjectReferences { get; }
}