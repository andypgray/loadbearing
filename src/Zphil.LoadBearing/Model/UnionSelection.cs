namespace Zphil.LoadBearing.Model;

/// <summary>
///     An internal, Freeze-only union of selections (GRAMMAR §7). There is no surface union
///     combinator in v1; this exists solely so the containment desugaring can feed <c>sel ∪ F</c>
///     into an <c>Except</c> payload. It is only ever rendered in reference position (the renderer
///     special-cases it), never as a sentence subject — so it has no single noun.
/// </summary>
internal sealed class UnionSelection : Selection
{
    internal UnionSelection(Arch owner, IReadOnlyList<Selection> parts)
        : base(owner)
    {
        Parts = parts;
    }

    /// <summary>The unioned selections, in order (named <c>Parts</c> to stay clear of the <c>.Members</c> projection, §4.6).</summary>
    internal IReadOnlyList<Selection> Parts { get; }

    internal override SelectionNoun Noun
        => throw new InvalidOperationException("A union selection has no single noun; render it in reference position.");

    internal override IReadOnlyList<SelectionAdjective> Adjectives => Array.Empty<SelectionAdjective>();
}