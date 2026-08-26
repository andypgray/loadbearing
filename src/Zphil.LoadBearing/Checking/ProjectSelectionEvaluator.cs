using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Fluent;
using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing.Checking;

/// <summary>
///     Resolves a <see cref="ProjectSelection" /> to the ordered list of <see cref="ProjectNode" />s it
///     names (GRAMMAR §4.10): every project the solution declares, narrowed by each project adjective in
///     authoring order.
/// </summary>
/// <remarks>
///     There is no noun to pick a candidate set with and no position to pick a universe with — a project
///     is only ever a subject, and the universe is always <see cref="CodebaseModel.Projects" />, which the
///     extractor already orders by name. So resolution is the adjective fold alone, and the result keeps
///     that order, which is what makes violations deterministic without a re-sort.
/// </remarks>
internal static class ProjectSelectionEvaluator
{
    /// <summary>The projects the selection ranges over, in <paramref name="projects" /> order.</summary>
    internal static IReadOnlyList<ProjectNode> Resolve(
        ProjectSelection selection, IReadOnlyList<ProjectNode> projects)
    {
        IEnumerable<ProjectNode> current = projects;
        foreach (ProjectAdjective adjective in selection.Adjectives) current = ApplyAdjective(current, adjective, projects);

        return current.ToList();
    }

    private static IEnumerable<ProjectNode> ApplyAdjective(
        IEnumerable<ProjectNode> current, ProjectAdjective adjective, IReadOnlyList<ProjectNode> projects)
    {
        switch (adjective)
        {
            case ProjectNamedAdjective named:
                var names = new HashSet<string>(named.Names, StringComparer.Ordinal);
                return current.Where(project => names.Contains(project.Name));
            case ProjectMatchingAdjective matching:
                IReadOnlyList<string> globs = matching.Globs;
                return current.Where(project => MatchesAnyGlob(globs, project.Name));
            case ProjectPackableAdjective:
                // Known-true only: an unevaluated project is not admitted, because unknown is not a claim
                // either way. The verbs hold the same tri-state honesty from the other side — they pass what
                // they do not know — so a rule over `.Packable()` projects speaks about measured facts only.
                return current.Where(project => project.IsPackable == true);
            case ProjectExceptAdjective except:
                // Resolved eagerly, before the lazy Where below closes over it: the payload is a selection in
                // its own right and is evaluated against the same full universe, exactly as the type side
                // evaluates an Except payload in target position.
                var excluded = new HashSet<ProjectNode>(Resolve(except.Payload, projects));
                return current.Where(project => !excluded.Contains(project));
            case ProjectWhereAdjective where:
                return current.Where(project => SelectionEvaluator.InvokePredicate(where.Predicate, project, "Where"));
            default:
                // Fail closed: an unknown project adjective would silently widen the selection, and a widened
                // subject means a law reaching projects its author never named. A missing arm is a bug; throw
                // (ArchChecker contains it per-rule) rather than pass the un-narrowed set through.
                throw new InvalidOperationException($"Unhandled project adjective '{adjective.GetType().Name}'.");
        }
    }

    // An index walk rather than a LINQ Any, because it runs once per project over a list that is usually a
    // single glob — the same shape the layer glob scan takes.
    private static bool MatchesAnyGlob(IReadOnlyList<string> globs, string name)
    {
        for (var i = 0; i < globs.Count; i++)
            if (Wildcard.Match(globs[i], name))
                return true;

        return false;
    }
}
