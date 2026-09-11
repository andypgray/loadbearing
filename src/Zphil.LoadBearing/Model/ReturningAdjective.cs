using Zphil.LoadBearing.Fluent;
using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.Returning(typeof(Task), ...)</c> on a <see cref="MethodSelection" /> → " returning `Task`"
///     (GRAMMAR §4.6, §5.7). Holds the anchors in authoring order, each a <c>typeof</c> or a type
///     definition's fully-qualified name; the fragment backticks each anchor's simple name (generics via
///     declared type-parameter names, so <c>typeof(Task&lt;&gt;)</c> renders <c>Task&lt;TResult&gt;</c>),
///     widens colliding simple names outward (GRAMMAR §6) and joins with the no-Oxford-comma reference
///     join. Matching is definition-level and happens at check time; on the <c>typeof</c> arm a
///     closed-generic anchor is refused at spec build (GRAMMAR §8 item 14).
/// </summary>
internal sealed class ReturningAdjective(IReadOnlyList<TypeAnchor> anchors) : MemberAdjective
{
    /// <summary>The return-type anchors, in authoring order.</summary>
    internal IReadOnlyList<TypeAnchor> Anchors { get; } = anchors;

    internal override AdjectivePlacement Placement => AdjectivePlacement.Inline;

    internal override string Fragment => " returning " + ProseFormat.AnchorList(Anchors);
}
