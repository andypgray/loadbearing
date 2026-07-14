namespace Zphil.LoadBearing.Model;

/// <summary>
///     Every non-bare-layer selection: a noun plus one or more adjectives (GRAMMAR §6). Produced
///     by the noun factories on <see cref="Arch" /> (with an empty adjective list) and by every
///     adjective extension (which appends one adjective and re-stamps the owner).
/// </summary>
internal sealed class RefinedSelection : Selection
{
    internal RefinedSelection(Arch owner, SelectionNoun noun, IReadOnlyList<SelectionAdjective> adjectives)
        : base(owner)
    {
        Noun = noun;
        Adjectives = adjectives;
    }

    internal override SelectionNoun Noun { get; }

    internal override IReadOnlyList<SelectionAdjective> Adjectives { get; }
}