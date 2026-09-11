using Zphil.LoadBearing.Hosting;

namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     Where a layer's local-rules card goes: the layer's name and purpose, the rules the card lists,
///     and either the directory whose <c>AGENTS.md</c> receives it or a null path with the reason it was
///     skipped.
/// </summary>
public sealed class LayerPlacement
{
    internal LayerPlacement(
        string layerName, string? purpose, IReadOnlyList<ArchRule> rules, string? directoryPath, string? skipReason)
    {
        LayerName = layerName;
        Purpose = purpose;
        Rules = rules;
        DirectoryPath = directoryPath;
        SkipReason = skipReason;
    }

    /// <summary>
    ///     Gets the layer's name as the spec declares it, <c>Web</c> say.
    /// </summary>
    public string LayerName { get; }

    /// <summary>
    ///     Gets what the layer is for, as the spec states it, carried into the card's lede; null when the layer was
    ///     given no purpose.
    /// </summary>
    public string? Purpose { get; }

    /// <summary>
    ///     Gets the Enforce and Migrate rules the layer is the subject of, in model order, one card bullet each.
    /// </summary>
    public IReadOnlyList<ArchRule> Rules { get; }

    /// <summary>
    ///     Gets the directory whose <c>AGENTS.md</c> receives the layer card, which is the deepest one holding every
    ///     file that declares one of the layer's types, or null when the layer matched no type in the solution. Then
    ///     <see cref="SkipReason" /> says so.
    /// </summary>
    public string? DirectoryPath { get; }

    /// <summary>
    ///     Gets why no directory was resolved, set exactly when <see cref="DirectoryPath" /> is null. A host with
    ///     somewhere to put it prints it as a warning.
    /// </summary>
    public string? SkipReason { get; }
}
