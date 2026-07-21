using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.Returning(typeof(Task), ...)</c> on a <see cref="MethodSelection" /> → " returning `Task`"
///     (GRAMMAR §4.6, §5.7). Holds the anchor types in authoring order; the fragment backticks each
///     anchor's simple name (generics via declared type-parameter names, so <c>typeof(Task&lt;&gt;)</c>
///     renders <c>Task&lt;TResult&gt;</c>) and joins with the no-Oxford-comma reference join. Matching
///     is definition-level and happens at check time; a closed-generic anchor is refused at spec
///     build (GRAMMAR §8 item 14).
/// </summary>
internal sealed class ReturningAdjective(IReadOnlyList<Type> types) : MemberAdjective
{
    /// <summary>The return-type anchors, in authoring order.</summary>
    internal IReadOnlyList<Type> Types { get; } = types;

    internal override AdjectivePlacement Placement => AdjectivePlacement.Inline;

    internal override string Fragment =>
        " returning " + ProseFormat.JoinReferences(Types.Select(type => ProseFormat.Backtick(TypeName.Simple(type))).ToList());
}