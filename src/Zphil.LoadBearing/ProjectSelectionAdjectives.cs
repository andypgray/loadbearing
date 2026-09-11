using Zphil.LoadBearing.Fluent;
using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Model;
using static Zphil.LoadBearing.Internal.Guard;

namespace Zphil.LoadBearing;

/// <summary>
///     The v1 project-adjective vocabulary (GRAMMAR §4.10) as extension methods on
///     <see cref="ProjectSelection" />.
/// </summary>
/// <remarks>
///     Each appends one closed-vocabulary adjective and returns a fresh selection carrying the same
///     <see cref="Arch" /> owner. Project selections are immutable values; each call yields a new one, so
///     a selection can be reused and refined in different directions. These bind by receiver type — a
///     <see cref="ProjectSelection" /> is neither a <see cref="Selection" /> nor a
///     <see cref="MemberSelection" /> — so the identically-named type- and member-side adjectives never
///     collide on overload resolution.
/// </remarks>
public static class ProjectSelectionAdjectives
{
    /// <summary>
    ///     Narrows to the projects named exactly, substituting the subject head: "project
    ///     `Zphil.LoadBearing`", or "projects `A` or `B`" for several. Ordinal, case-sensitive — a project
    ///     name is an identifier the solution declares, so <see cref="Matching" /> is the glob form beside
    ///     this one. The <c>(first, more)</c> shape makes a zero-name call uncompilable.
    /// </summary>
    public static ProjectSelection Named(this ProjectSelection selection, string first, params string[] more)
    {
        NotNull(more, nameof(more));
        IReadOnlyList<string> names = OperandList.OneOrMore(first, more, name => name);
        return Append(selection, new ProjectNamedAdjective(names));
    }

    /// <summary>
    ///     Narrows to the projects whose name matches a glob: " matching `Zphil.*`", or-joined for
    ///     several. <c>*</c> matches any run of characters; every other character is an ordinal match, and
    ///     a project name is one token with no dot-segment structure.
    /// </summary>
    public static ProjectSelection Matching(this ProjectSelection selection, string glob, params string[] more)
    {
        NotNull(more, nameof(more));
        IReadOnlyList<string> globs = OperandList.OneOrMore(glob, more, pattern => pattern);
        return Append(selection, new ProjectMatchingAdjective(globs));
    }

    /// <summary>
    ///     Narrows to the projects that produce a package, premodifying the subject head: "packable
    ///     projects" (GRAMMAR §4.10, §6). Known-true only — a project whose <c>IsPackable</c> nothing
    ///     evaluated is not admitted, because unknown is not a claim either way.
    /// </summary>
    public static ProjectSelection Packable(this ProjectSelection selection)
    {
        return Append(selection, new ProjectPackableAdjective());
    }

    /// <summary>
    ///     Excludes another project selection; canonicalized to sentence-final, after any <c>Where</c> on
    ///     the same selection (GRAMMAR §6).
    /// </summary>
    public static ProjectSelection Except(this ProjectSelection selection, ProjectSelection exclusion)
    {
        return Append(selection, new ProjectExceptAdjective(NotNull(exclusion, nameof(exclusion))));
    }

    /// <summary>
    ///     The project selector-position escape hatch. The predicate is stored, never evaluated at spec
    ///     build; the required <paramref name="description" /> is what renders as a sentence-final relative
    ///     clause (GRAMMAR §5.6). A blank description fails spec build (validation §8 item 5).
    /// </summary>
    public static ProjectSelection Where(
        this ProjectSelection selection, Func<IProjectInfo, bool> predicate, string description)
    {
        return Append(selection, new ProjectWhereAdjective(NotNull(predicate, nameof(predicate)), description));
    }

    private static ProjectSelection Append(ProjectSelection selection, ProjectAdjective adjective)
    {
        NotNull(selection, nameof(selection));
        return selection.Refined(adjective);
    }
}
