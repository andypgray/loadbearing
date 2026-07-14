using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.Except(selection)</c> → ", except {reference}", canonicalized to sentence-final
///     (GRAMMAR §5.2, §6). The payload renders in reference position via the renderer.
/// </summary>
internal sealed class ExceptAdjective(Selection payload) : SelectionAdjective
{
    /// <summary>The excluded selection.</summary>
    internal Selection Payload { get; } = payload;

    internal override AdjectivePlacement Placement => AdjectivePlacement.SubjectFinal;

    internal override string Fragment => ", except " + SentenceRenderer.Reference(Payload);
}