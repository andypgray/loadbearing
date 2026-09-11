namespace Zphil.LoadBearing.Hosting;

/// <summary>
///     A layer the spec declared, in the finished model: its name, the namespaces it covers, what it is
///     for, and the line the generated agent context prints for it.
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

    /// <summary>Gets the layer's name as the spec declared it.</summary>
    public string Name { get; }

    /// <summary>
    ///     Gets the namespace globs that define the layer: the globs the spec listed, or — for a layer
    ///     defined by a selection instead — the one namespace that selection names, or empty where it names
    ///     no single namespace.
    /// </summary>
    public IReadOnlyList<string> Globs { get; }

    /// <summary>
    ///     The selection that defines the layer, or null for the glob form — what the law drawing reads to
    ///     place the layer and to collapse it onto what it is defined as.
    /// </summary>
    internal Selection? Definition { get; }

    /// <summary>
    ///     Gets the line the generated agent context prints for the layer, such as
    ///     <c>**Domain** — `MyApp.Domain.*`</c>, with the purpose appended as its own sentence where the
    ///     layer has one.
    /// </summary>
    public string DefinitionFragment { get; }

    /// <summary>Gets what the layer is for, in the spec's own words, or null where the spec gave none.</summary>
    public string? Purpose { get; }
}
