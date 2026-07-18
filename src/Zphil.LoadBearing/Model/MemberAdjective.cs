namespace Zphil.LoadBearing.Model;

/// <summary>
///     A refinement on a <see cref="MemberSelection" /> — a closed hierarchy so each member adjective
///     owns its own prose fragment (GRAMMAR §2 admission rule, §4.6). Reuses
///     <see cref="AdjectivePlacement" />: member adjectives are either <see cref="AdjectivePlacement.Inline" />
///     reduced relative clauses or the sentence-final <c>Where</c>; there is no head substitution
///     (the projection fixes the head, §6).
/// </summary>
internal abstract class MemberAdjective
{
    /// <summary>Where this adjective's fragment lands during member-subject assembly (GRAMMAR §6).</summary>
    internal abstract AdjectivePlacement Placement { get; }

    /// <summary>The rendered fragment, including its leading separator (e.g. <c> returning `Task`</c>).</summary>
    internal abstract string Fragment { get; }
}