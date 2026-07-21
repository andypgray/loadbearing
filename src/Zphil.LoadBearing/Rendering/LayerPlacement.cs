namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     Where a layer's "local rules" context card lands: the layer name, the anchored
///     Enforce/Migrate rules whose subject noun head is that layer (in model order — the
///     <see cref="AgentContextRenderer.LayerCard" /> source), and either the resolved
///     <see cref="DirectoryPath" /> — the deepest common ancestor of the layer's types' declaration
///     sites — or a null path with a <see cref="SkipReason" /> when the layer matched no types.
/// </summary>
public sealed class LayerPlacement
{
    internal LayerPlacement(string layerName, IReadOnlyList<ArchRule> rules, string? directoryPath, string? skipReason)
    {
        LayerName = layerName;
        Rules = rules;
        DirectoryPath = directoryPath;
        SkipReason = skipReason;
    }

    /// <summary>The declaring layer's name (e.g. <c>Web</c>).</summary>
    public string LayerName { get; }

    /// <summary>The anchored Enforce/Migrate rules rendered into the layer card, in model order.</summary>
    public IReadOnlyList<ArchRule> Rules { get; }

    /// <summary>
    ///     The directory whose <c>AGENTS.md</c> receives the layer card, or null when the layer
    ///     matched no solution types (then <see cref="SkipReason" /> explains the skip).
    /// </summary>
    public string? DirectoryPath { get; }

    /// <summary>The skip explanation (for a stderr warning) iff <see cref="DirectoryPath" /> is null.</summary>
    public string? SkipReason { get; }
}