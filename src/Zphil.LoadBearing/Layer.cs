using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing;

/// <summary>
///     A named architectural layer — itself a <see cref="Selection" />, so it can be used directly
///     as a rule subject or a reference target with no <c>.Types</c> hop (GRAMMAR §12). A bare
///     layer with no adjectives speaks in the collective voice ("The Domain layer …", GRAMMAR §6);
///     any adjective returns a <see cref="Model.RefinedSelection" /> and switches to types voice.
/// </summary>
public sealed class Layer : Selection
{
    private readonly LayerNoun _noun;

    internal Layer(Arch owner, LayerNoun noun)
        : base(owner)
    {
        _noun = noun;
    }

    internal override SelectionNoun Noun => _noun;

    internal override IReadOnlyList<SelectionAdjective> Adjectives => Array.Empty<SelectionAdjective>();
}