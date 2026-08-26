using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.Named(name, …)</c> → the head itself: "project `A`" for one name, "projects `A` or `B`" for
///     several (GRAMMAR §4.10, §6). Matching is exact and ordinal — a project name is an identifier the
///     solution declares, not a pattern, so <c>.Matching</c> is the glob form beside it.
/// </summary>
/// <remarks>
///     Head substitution rather than an inline clause because the names ARE the noun once given: "projects
///     named `A`" says the same thing at more length, and the substituted head keeps composing with the
///     <see cref="ProjectPackableAdjective" /> prefix.
/// </remarks>
internal sealed class ProjectNamedAdjective(IReadOnlyList<string> names) : ProjectAdjective
{
    /// <summary>The exact project names, in authoring order.</summary>
    internal IReadOnlyList<string> Names { get; } = names;

    internal override AdjectivePlacement Placement => AdjectivePlacement.Head;

    internal override string Fragment
    {
        get
        {
            List<string> backticked = Names.Select(ProseFormat.Backtick).ToList();
            string head = backticked.Count == 1 ? "project " : "projects ";
            return head + ProseFormat.JoinReferences(backticked);
        }
    }
}
