namespace Zphil.LoadBearing.Model;

/// <summary>
///     A refinement on a <see cref="Selection" /> — a closed hierarchy so each adjective owns its
///     own prose fragment (GRAMMAR §2 admission rule). <see cref="Placement" /> tells the renderer
///     where the <see cref="Fragment" /> lands during subject assembly (GRAMMAR §6).
/// </summary>
internal abstract class SelectionAdjective
{
    /// <summary>Where this adjective's fragment lands during subject assembly.</summary>
    internal abstract AdjectivePlacement Placement { get; }

    /// <summary>
    ///     The rendered fragment, including its leading separator for inline/subject-final
    ///     placements (e.g. <c> in `MyApp.*`</c>, <c>, except `Foo`</c>); for <see cref="Placement" />
    ///     of <see cref="AdjectivePlacement.Head" /> this is the bare plural that replaces the head.
    /// </summary>
    internal abstract string Fragment { get; }
}