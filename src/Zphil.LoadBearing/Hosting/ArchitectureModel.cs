namespace Zphil.LoadBearing.Hosting;

/// <summary>
///     The finalized, walkable architecture model — the single reified spec both render targets
///     consume. Returned by <see cref="ArchModelBuilder.Build(IArchitectureSpec[])" />
///     once the spec passes the whole validation catalog.
/// </summary>
public sealed class ArchitectureModel
{
    internal ArchitectureModel(IReadOnlyList<ArchRule> rules, IReadOnlyList<LayerDefinition> layers)
    {
        Rules = rules;
        Layers = layers;
    }

    /// <summary>
    ///     Every rule, in authoring order, with each scope's generated children — a quarantine's
    ///     containment then tripwire, a caution's tripwire alone — sitting at the scope's position
    ///     (GRAMMAR §7).
    /// </summary>
    public IReadOnlyList<ArchRule> Rules { get; }

    /// <summary>Every declared layer, in authoring order.</summary>
    public IReadOnlyList<LayerDefinition> Layers { get; }
}
