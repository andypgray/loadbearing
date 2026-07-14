namespace Zphil.LoadBearing;

/// <summary>
///     A declared layer in the read model: its name, its globs, and the rendered definition
///     fragment (GRAMMAR §5.1) — e.g. <c>**Domain** — `MyApp.Domain.*`</c>.
/// </summary>
public sealed class LayerDefinition
{
    internal LayerDefinition(string name, IReadOnlyList<string> globs, string definitionFragment)
    {
        Name = name;
        Globs = globs;
        DefinitionFragment = definitionFragment;
    }

    /// <summary>The layer name.</summary>
    public string Name { get; }

    /// <summary>The namespace globs that define the layer.</summary>
    public IReadOnlyList<string> Globs { get; }

    /// <summary>The rendered definition fragment for the module map.</summary>
    public string DefinitionFragment { get; }
}