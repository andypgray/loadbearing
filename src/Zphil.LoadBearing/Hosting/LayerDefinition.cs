namespace Zphil.LoadBearing.Hosting;

/// <summary>
///     A declared layer in the read model: its name, its globs, its optional purpose, and the rendered
///     definition fragment (GRAMMAR §5.1) — e.g. <c>**Domain** — `MyApp.Domain.*`</c>, or
///     <c>**Domain** — `MyApp.Domain.*`. The quote and rate-card model.</c> when the layer has a purpose.
/// </summary>
public sealed class LayerDefinition
{
    internal LayerDefinition(
        string name, IReadOnlyList<string> globs, string definitionFragment, string? purpose, Selection? definition)
    {
        Name = name;
        Globs = globs;
        DefinitionFragment = definitionFragment;
        Purpose = purpose;
        Definition = definition;
    }

    /// <summary>The layer name.</summary>
    public string Name { get; }

    /// <summary>
    ///     The namespace globs that define the layer: the glob form's list, or the namespace region a
    ///     selection definition names, or empty when it names none.
    /// </summary>
    public IReadOnlyList<string> Globs { get; }

    /// <summary>
    ///     The selection that defines the layer, or null for the glob form — what the law drawing reads to
    ///     place the layer and to collapse it onto what it is defined as.
    /// </summary>
    internal Selection? Definition { get; }

    /// <summary>The rendered definition fragment for the module map.</summary>
    public string DefinitionFragment { get; }

    /// <summary>What the layer is for, as authored, or null when the spec gave it no purpose.</summary>
    public string? Purpose { get; }
}
