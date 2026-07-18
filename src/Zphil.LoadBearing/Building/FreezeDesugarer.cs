using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Model;
using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Building;

/// <summary>
///     Desugars a frozen scope into two ordinary posture-bearing rule nodes (GRAMMAR §7): a
///     containment rule (<c>{id}/containment</c>) carrying the boundary/baseline and the
///     <c>sel.Except(F).MustOnlyBeReferencedBy(sel ∪ F)</c> predicate, and a tripwire rule
///     (<c>{id}/tripwire</c>) that carries the frozen selection for diff-touch matching but has no
///     closed-vocabulary constraint. Clause distribution follows §7: Because and dragons go to both
///     children; BoundaryOnlyVia and Baseline go to containment. An omitted <c>.Baseline</c> fills the
///     containment child with the conventional default <c>arch/baselines/{id}/containment.json</c>
///     (GRAMMAR §4.4/§7), so the containment baseline is never null post-build; the tripwire's stays null.
/// </summary>
internal static class FreezeDesugarer
{
    internal static IReadOnlyList<ArchRule> Desugar(ScopeRegistration scope)
    {
        Selection frozen = scope.Frozen!;
        Arch owner = frozen.Owner;
        IReadOnlyList<Type> boundary = scope.Boundary;
        string because = First(scope.Becauses);
        // .Baseline(path) omitted ⇒ the conventional default derived from the containment rule ID
        // (GRAMMAR §4.4/§7), so the containment child's baseline is never null post-build.
        string baseline = FirstOrNull(scope.Baselines) ?? BaselineConventions.DefaultPath(scope.Id + "/containment");
        string? dragons = FirstOrNull(scope.Dragons);
        string? dragonsDoc = FirstOrNull(scope.DragonsDocs);

        Constraint containment = BuildContainment(owner, frozen, boundary);
        string? fix = boundary.Count > 0
            ? "use " + ProseFormat.Backtick(TypeName.Simple(boundary[0]))
            : null;

        var containmentRule = new ArchRule(
            scope.Id + "/containment",
            Posture.Freeze,
            because,
            fix,
            SentenceRenderer.Sentence(containment),
            containment,
            null,
            new FreezeData(FreezeRole.Containment, boundary, baseline, dragons, dragonsDoc, scope.Id, frozen));

        var tripwireRule = new ArchRule(
            scope.Id + "/tripwire",
            Posture.Freeze,
            because,
            null,
            string.Empty,
            null,
            null,
            new FreezeData(FreezeRole.Tripwire, Array.Empty<Type>(), null, dragons, dragonsDoc, scope.Id, frozen));

        return [containmentRule, tripwireRule];
    }

    private static Constraint BuildContainment(Arch owner, Selection frozen, IReadOnlyList<Type> boundary)
    {
        if (boundary.Count == 0)
            // Hermetic freeze: nothing outside the scope may reference it (GRAMMAR §7).
            return frozen.MustOnlyBeReferencedBy(frozen);

        var facades = boundary
            .Select(type => (Selection)new RefinedSelection(owner, new TypeNoun(type), Array.Empty<SelectionAdjective>()))
            .ToList();

        // sel.Except(F) . MustOnlyBeReferencedBy(sel, F...) — the formula holds whether the
        // facade types live inside or outside the frozen selection (GRAMMAR §7).
        var union = new UnionSelection(owner, facades);
        Selection subject = frozen.Except(union);
        return subject.MustOnlyBeReferencedBy(frozen, facades.ToArray());
    }

    private static string First(List<string> values)
    {
        return values.Count > 0 ? values[0] : string.Empty;
    }

    private static string? FirstOrNull(List<string> values)
    {
        return values.Count > 0 ? values[0] : null;
    }
}