using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.Implementing(typeof(IHandler&lt;&gt;))</c> → " implementing `IHandler&lt;T&gt;`"
///     (GRAMMAR §5.2). An open generic definition means any construction; rendering uses declared
///     type-parameter names. The anchor is a <see cref="TypeAnchor" />, so the string form
///     (<c>.Implementing("MyApp.Web.IHandler&lt;T&gt;")</c>) renders the identical fragment.
/// </summary>
internal sealed class ImplementingAdjective(TypeAnchor anchor) : SelectionAdjective
{
    /// <summary>The interface the subject must implement, by <c>typeof</c> or by definition name.</summary>
    internal TypeAnchor Anchor { get; } = anchor;

    internal override AdjectivePlacement Placement => AdjectivePlacement.Inline;

    internal override string Fragment => $" implementing {ProseFormat.Backtick(ProseFormat.AnchorName(Anchor))}";
}
