using Zphil.LoadBearing.Fluent;
using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Model;
using static Zphil.LoadBearing.Internal.Guard;

namespace Zphil.LoadBearing;

/// <summary>
///     The adjectives that narrow a <see cref="ProjectSelection" /> — the solution's projects as build
///     artifacts, from <c>arch.Projects</c>. Each returns a new selection carrying one more condition
///     and leaves the original untouched, so one selection can be assigned to a variable and refined
///     in several directions. Finish with a project verb such as <c>MustOnlyTarget</c>,
///     <c>MustReferenceNoPackages</c>, <c>MustLockPackages</c> or <c>MustNotBePackable</c>.
/// </summary>
// These bind by receiver type — a ProjectSelection is neither a Selection nor a MemberSelection — so
// the identically-named type- and member-side adjectives never collide on overload resolution
// (GRAMMAR §4.10).
public static class ProjectSelectionAdjectives
{
    /// <summary>
    ///     Narrows the selection to the projects with exactly these names, such as
    ///     <c>arch.Projects.Named("MyApp.Web")</c> or
    ///     <c>arch.Projects.Named("MyApp.Web", "MyApp.Api")</c>, which keeps the projects carrying either
    ///     name. The name is the one the solution lists, compared case-sensitively and taken literally, so
    ///     a <c>*</c> in it is a literal character; <see cref="Matching" /> is the glob form beside this
    ///     one. At least one name is required, and a blank one is reported when the spec is loaded.
    /// </summary>
    public static ProjectSelection Named(this ProjectSelection selection, string first, params string[] more)
    {
        NotNull(more, nameof(more));
        IReadOnlyList<string> names = OperandList.OneOrMore(first, more, name => name);
        return Append(selection, new ProjectNamedAdjective(names));
    }

    /// <summary>
    ///     Narrows the selection to the projects whose name matches a glob, such as
    ///     <c>arch.Projects.Matching("MyApp.Plugins.*")</c>; several globs keep the projects matching any
    ///     one of them. Matching is case-sensitive: <c>*</c> matches any run of characters including none,
    ///     and every other character matches itself. A project name is one token, so a <c>*</c> crosses
    ///     dots freely and there is no subtree operator of the kind a namespace glob has. At least one
    ///     glob is required, and a blank one is reported when the spec is loaded.
    /// </summary>
    public static ProjectSelection Matching(this ProjectSelection selection, string glob, params string[] more)
    {
        NotNull(more, nameof(more));
        IReadOnlyList<string> globs = OperandList.OneOrMore(glob, more, pattern => pattern);
        return Append(selection, new ProjectMatchingAdjective(globs));
    }

    /// <summary>
    ///     Narrows the selection to the projects that produce a NuGet package, such as
    ///     <c>arch.Projects.Packable()</c>. Packability is the evaluated <c>IsPackable</c> value, read
    ///     after SDK defaults and every import rather than out of the project file's text. A project whose
    ///     value nothing evaluated is left out, unknown being no claim either way — the opposite direction
    ///     from the project verbs, which pass what they do not know, so a rule over packable projects
    ///     speaks about measured projects alone.
    /// </summary>
    public static ProjectSelection Packable(this ProjectSelection selection)
    {
        return Append(selection, new ProjectPackableAdjective());
    }

    /// <summary>
    ///     Narrows the selection by removing the projects another project selection names, such as
    ///     <c>arch.Projects.Matching("MyApp.*").Except(arch.Projects.Named("MyApp.Tests"))</c>. One
    ///     exclusion, not a list: to exclude several projects, give the exclusion several names
    ///     (<c>Named("A", "B")</c>) or a glob. The exclusion is rendered last in the rule's sentence in
    ///     the generated agent context, after any <see cref="Where" /> on the same selection.
    /// </summary>
    public static ProjectSelection Except(this ProjectSelection selection, ProjectSelection exclusion)
    {
        return Append(selection, new ProjectExceptAdjective(NotNull(exclusion, nameof(exclusion))));
    }

    /// <summary>
    ///     Narrows the selection with a predicate of your own, for what the project adjectives cannot say:
    ///     <c>
    ///         arch.Projects.Where(p =&gt; p.ProjectReferences.Count &gt; 10, description: "with more than
    ///         ten project references")
    ///     </c>
    ///     . The predicate reads the facts on <see cref="IProjectInfo" /> and
    ///     runs against each candidate project when the check runs, never when the spec is loaded. A fact
    ///     nothing evaluated is <see langword="null" /> there, so a predicate that reads null as false
    ///     asserts something nothing measured. <paramref name="description" /> is required and completes
    ///     the subject as a relative clause; it is rendered verbatim in the generated agent context in
    ///     place of the wording an adjective would produce, last in the clause list, and nothing checks
    ///     that it describes what the predicate does. A blank or multi-line description is reported when
    ///     the spec is loaded.
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
