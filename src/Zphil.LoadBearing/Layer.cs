using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing;

/// <summary>
///     A named architectural layer — itself a <see cref="Selection" />, so it can be used directly
///     as a rule subject or a reference target with no <c>.Types</c> hop (GRAMMAR §12). A bare
///     layer with no adjectives speaks in the collective voice ("The Domain layer …", GRAMMAR §6);
///     any adjective returns a <see cref="Model.RefinedSelection" /> and switches to types voice. The
///     definition carries an optional <see cref="Purpose" /> trailer saying what the layer is for.
/// </summary>
public sealed class Layer : Selection
{
    private readonly LayerNoun _noun;

    internal Layer(Arch owner, LayerNoun noun)
        : base(owner)
    {
        _noun = noun;
    }

    /// <summary>
    ///     What the layer is for — one sentence, rendered after the definition in the module-map row and after
    ///     the first sentence of the layer card's lede (GRAMMAR §5.5). Optional; at most one, and never a
    ///     card on its own: a card is placed only where a rule anchors on the layer.
    /// </summary>
    public Layer Purpose(string prose)
    {
        Owner.AddLayerPurpose(_noun, prose);
        return this;
    }

    internal string Name => _noun.Name;

    internal override SelectionNoun Noun => _noun;

    internal override IReadOnlyList<SelectionAdjective> Adjectives => Array.Empty<SelectionAdjective>();
}
