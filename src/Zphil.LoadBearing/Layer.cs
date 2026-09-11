using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing;

/// <summary>
///     A named layer: a <see cref="Selection" /> that also carries a name, defined with the
///     <c>Layer</c> methods on <see cref="Arch" />. Use it directly as a rule subject or target, where
///     a rule reads as a sentence about the layer ("The Domain layer must not reference ..."); an
///     adjective narrows it and returns an ordinary selection. Call <see cref="Purpose" /> to say what
///     the layer is for.
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
    ///     States what the layer is for, in one line of prose, such as
    ///     <c>arch.Layer("Domain", "MyApp.Domain.*").Purpose("Business rules; no I/O.")</c>. Rendered as
    ///     written after the layer's definition in the generated agent context. Optional and at most
    ///     once; a blank or multi-line value, or a second call, is reported when the spec is loaded.
    ///     Returns the same layer.
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
