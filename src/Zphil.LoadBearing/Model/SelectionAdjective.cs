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

    /// <summary>
    ///     Whether the fragment opens a parenthetical it does not close — an <c>Except</c> clause — so the
    ///     composer closes it with a comma at whatever junction follows (GRAMMAR §6). Meaningful only for a
    ///     <see cref="AdjectivePlacement.SubjectFinal" /> placement, the one that can end a phrase.
    /// </summary>
    internal virtual bool OpensParenthetical => false;
}
