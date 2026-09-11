using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.Except(selection, …)</c> → ", except {reference}", canonicalized to sentence-final and after
///     any <c>Where</c> of the same subject (GRAMMAR §5.2, §6). The payload renders in reference position
///     via the renderer.
/// </summary>
/// <remarks>
///     The fragment opens a parenthetical and never closes it: the renderer is position-blind here — the
///     same phrase serves subject and reference position — so the closing comma belongs to the junction that
///     knows what follows (<see cref="SentenceRenderer" />), and a trailing comma in this fragment would
///     land before a period or mid-list. Rendering last within the sentence-final group is what keeps that
///     junction adjacent: a <c>Where</c> after it would read as a description of the exception rather than
///     of the subject.
/// </remarks>
internal sealed class ExceptAdjective(Selection payload) : SelectionAdjective
{
    /// <summary>The excluded selection.</summary>
    internal Selection Payload { get; } = payload;

    internal override AdjectivePlacement Placement => AdjectivePlacement.SubjectFinal;

    internal override bool OpensParenthetical => true;

    internal override string Fragment => ", except " + SentenceRenderer.Reference(Payload);
}
