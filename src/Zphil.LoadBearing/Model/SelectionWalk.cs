using Zphil.LoadBearing.Fluent;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     The canonical walk over every selection a constraint reaches — subject, operands, union parts and
///     <c>Except</c> payloads — held in one place so validation and rendering agree on what a rule
///     talks about.
/// </summary>
/// <remarks>
///     Enumeration order is part of the contract: validation reports its errors in walk order, so a
///     reordering here reorders diagnostics.
/// </remarks>
internal static class SelectionWalk
{
    /// <summary>The constraint's subject tree followed by each operand's tree, in operand order.</summary>
    /// <remarks>
    ///     A project constraint contributes no type selection at all — its subject is a project selection
    ///     and its <see cref="Constraint.Subject" /> is null (GRAMMAR §4.10) — so it walks out empty here
    ///     and through <see cref="ConstraintProjectSelections" /> instead. Nothing about the type-side walk
    ///     changes: a rule with no type selections simply has none to report.
    /// </remarks>
    internal static IEnumerable<Selection> ConstraintSelections(Constraint constraint)
    {
        if (constraint.Subject is { } subject)
            foreach (Selection selection in ExpandSelection(subject))
                yield return selection;

        foreach (Selection operand in constraint.Operands)
        foreach (Selection selection in ExpandSelection(operand))
            yield return selection;
    }

    /// <summary>
    ///     The project selections a constraint reaches — its project subject and the payloads nested under
    ///     that subject's <c>Except</c> adjectives — or nothing at all for every type- and member-subject
    ///     constraint. The project-stratum twin of <see cref="ConstraintSelections" />, kept beside it so
    ///     the two walks a validation pass needs are read in one place.
    /// </summary>
    internal static IEnumerable<ProjectSelection> ConstraintProjectSelections(Constraint constraint)
    {
        if (constraint is not ProjectConstraint project) yield break;

        foreach (ProjectSelection selection in ExpandProjectSelection(project.ProjectSubject)) yield return selection;
    }

    /// <summary>The project selection itself, then the payloads of its own <c>Except</c> adjectives.</summary>
    /// <remarks>
    ///     There is no union arm: the project stratum has no <c>AnyOf</c>, because <c>.Named(a, b)</c> and
    ///     <c>.Matching(a, b)</c> already say "either of these" without one. <c>Except</c> is the only way a
    ///     project selection nests.
    /// </remarks>
    internal static IEnumerable<ProjectSelection> ExpandProjectSelection(ProjectSelection selection)
    {
        yield return selection;

        foreach (ProjectAdjective adjective in selection.Adjectives)
            if (adjective is ProjectExceptAdjective except)
                foreach (ProjectSelection nested in ExpandProjectSelection(except.Payload))
                    yield return nested;
    }

    /// <summary>The selection itself, then its union parts, then the payloads of its own <c>Except</c> adjectives.</summary>
    internal static IEnumerable<Selection> ExpandSelection(Selection selection)
    {
        yield return selection;

        // As in SelectionProse: the operands, then this selection's own Except payloads — a union carries
        // adjectives of its own, so AnyOf(a, b).Except(bad) must reach the payload walk.
        if (selection is UnionSelection union)
            foreach (Selection member in union.Parts)
            foreach (Selection nested in ExpandSelection(member))
                yield return nested;

        foreach (SelectionAdjective adjective in selection.Adjectives)
            if (adjective is ExceptAdjective except)
                foreach (Selection nested in ExpandSelection(except.Payload))
                    yield return nested;
    }
}
