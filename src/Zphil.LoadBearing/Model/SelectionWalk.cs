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
        if (constraint is ProjectConstraint project)
        {
            foreach (ProjectSelection selection in ExpandProjectSelection(project.ProjectSubject)) yield return selection;

            yield break;
        }

        // The one project selection a TYPE-subject constraint can carry: the project form of a family
        // (GRAMMAR §5.1), whose cells the artifact stratum names. It rides this walk so the project-side
        // checks — the foreign-Arch walk and the blank name/glob check (§8 items 22–23) — reach it
        // without a walk of their own. A reader here is therefore not entitled to assume a project
        // constraint; each one reads the anchor it was given rather than the constraint.
        if (FamilyProjects(constraint.Subject) is { } projects)
            foreach (ProjectSelection selection in ExpandProjectSelection(projects))
                yield return selection;
    }

    /// <summary>
    ///     The noun head of a selection, or null for a <see cref="UnionSelection" /> and for null — the
    ///     one place the union guard is stated, because reading <see cref="Selection.Noun" /> on a union
    ///     throws by design (GRAMMAR §5.1). Every question about a selection's noun goes through it, so a
    ///     union answers "not that noun" rather than throwing, at every asker.
    /// </summary>
    internal static SelectionNoun? NounOf(Selection? selection)
    {
        return selection is null or UnionSelection ? null : selection.Noun;
    }

    /// <summary>The family noun of a family subject, or null for every other selection (GRAMMAR §5.1).</summary>
    internal static EachNoun? FamilyNoun(Selection? selection)
    {
        return NounOf(selection) as EachNoun;
    }

    /// <summary>
    ///     The project selection inside a family-of-projects subject, or null for every other selection —
    ///     the one place the type stratum reaches the artifact stratum (GRAMMAR §5.1).
    /// </summary>
    internal static ProjectSelection? FamilyProjects(Selection? selection)
    {
        return FamilyNoun(selection)?.Projects;
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

    /// <summary>
    ///     The selection itself, then its union parts or its family's layer cells, then the payloads of
    ///     its own <c>Except</c> adjectives.
    /// </summary>
    internal static IEnumerable<Selection> ExpandSelection(Selection selection)
    {
        yield return selection;

        // As in SelectionProse: the operands, then this selection's own Except payloads — a union carries
        // adjectives of its own, so AnyOf(a, b).Except(bad) must reach the payload walk. A family's layer
        // cells are walked on the same terms and for the same reason, so the foreign-Arch, lifetime and
        // blank walks reach a cell minted on another Arch (GRAMMAR §5.1, §8). The arms are exclusive
        // because reading .Noun on a union throws by design.
        if (selection is UnionSelection union)
            foreach (Selection member in union.Parts)
            foreach (Selection nested in ExpandSelection(member))
                yield return nested;
        else if (selection.Noun is EachNoun { Layers: { } cells })
            foreach (Layer cell in cells)
            foreach (Selection nested in ExpandSelection(cell))
                yield return nested;

        foreach (SelectionAdjective adjective in selection.Adjectives)
            if (adjective is ExceptAdjective except)
                foreach (Selection nested in ExpandSelection(except.Payload))
                    yield return nested;
    }
}
