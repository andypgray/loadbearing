using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.Matching(glob, …)</c> → " matching `Zphil.*`", or-joined for several globs (GRAMMAR §4.10, §6).
///     An inline reduced relative clause, so it narrows whatever head stands before it — the bare
///     "projects" or a <see cref="ProjectNamedAdjective" /> substitution alike.
/// </summary>
/// <remarks>
///     The glob is the shared <c>*</c> matcher's, ordinal and case-sensitive, with no dot-segment
///     structure: a project name is one token, so there is no subtree operator to strand a wildcard behind
///     and nothing here needs the namespace pattern's extra validation.
/// </remarks>
internal sealed class ProjectMatchingAdjective(IReadOnlyList<string> globs) : ProjectAdjective
{
    /// <summary>The name globs, in authoring order.</summary>
    internal IReadOnlyList<string> Globs { get; } = globs;

    internal override AdjectivePlacement Placement => AdjectivePlacement.Inline;

    internal override string Fragment =>
        " matching " + ProseFormat.JoinReferences(Globs.Select(ProseFormat.Backtick).ToList());
}
