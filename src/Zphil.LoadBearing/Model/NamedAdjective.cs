using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.Named(name, …)</c> → " named `Program`", or " named `A` or `B`" for several (GRAMMAR §5.2). An
///     inline reduced relative clause on the simple name, matched exact and ordinal — the wildcard-free
///     member of the family <c>WithSuffix</c> and <c>WithPrefix</c> already render with the same word.
/// </summary>
/// <remarks>
///     Inline rather than a bare backticked name because the fragment must state the set the checker
///     uses: a simple name reaches every type carrying it in every namespace and project, where a bare
///     "`X`" reads as the one type an <c>arch.Type</c> noun names.
/// </remarks>
internal sealed class NamedAdjective(IReadOnlyList<string> names) : SelectionAdjective
{
    /// <summary>The exact type names, in authoring order.</summary>
    internal IReadOnlyList<string> Names { get; } = names;

    internal override AdjectivePlacement Placement => AdjectivePlacement.Inline;

    internal override string Fragment => " named " + ProseFormat.BacktickedList(Names);
}
