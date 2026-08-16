using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Model;
using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Building;

/// <summary>
///     Desugars a quarantined scope into two ordinary posture-bearing rule nodes (GRAMMAR §7).
/// </summary>
/// <remarks>
///     A containment rule (<c>{id}/containment</c>) carries the boundary/baseline and the
///     <c>sel.Except(F).MustOnlyBeReferencedBy(sel ∪ F)</c> predicate; a tripwire rule
///     (<c>{id}/tripwire</c>) carries the quarantined selection for diff-touch matching but has no
///     closed-vocabulary constraint. Clause distribution follows §7: Because and dragons go to both
///     children; BoundaryOnlyVia and Baseline go to containment.
/// </remarks>
internal static class QuarantineDesugarer
{
    internal static IReadOnlyList<ArchRule> Desugar(ScopeRegistration scope)
    {
        Selection quarantined = scope.Quarantined!;
        Arch owner = quarantined.Owner;
        IReadOnlyList<Type> boundary = scope.Boundary;
        string because = First(scope.Becauses);
        // .Baseline(path) omitted ⇒ the conventional default derived from the containment rule ID
        // (GRAMMAR §4.4/§7), so the containment child's baseline is never null post-build.
        string baseline = FirstOrNull(scope.Baselines) ?? BaselineConventions.DefaultPath(scope.Id + "/containment");
        string? dragons = FirstOrNull(scope.Dragons);
        string? dragonsDoc = FirstOrNull(scope.DragonsDocs);

        Constraint containment = BuildContainment(owner, quarantined, boundary);
        string? fix = boundary.Count > 0
            ? "use " + ProseFormat.Backtick(TypeName.Simple(boundary[0]))
            : null;

        var containmentRule = new ArchRule(
            scope.Id + "/containment",
            Posture.Quarantine,
            because,
            fix,
            SentenceRenderer.Sentence(containment),
            containment,
            null,
            new QuarantineData(QuarantineRole.Containment, boundary, baseline, dragons, dragonsDoc, scope.Id, quarantined));

        var tripwireRule = new ArchRule(
            scope.Id + "/tripwire",
            Posture.Quarantine,
            because,
            null,
            string.Empty,
            null,
            null,
            new QuarantineData(QuarantineRole.Tripwire, Array.Empty<Type>(), null, dragons, dragonsDoc, scope.Id, quarantined));

        return [containmentRule, tripwireRule];
    }

    private static Constraint BuildContainment(Arch owner, Selection quarantined, IReadOnlyList<Type> boundary)
    {
        if (boundary.Count == 0)
            // Hermetic quarantine: nothing outside the scope may reference it (GRAMMAR §7).
            return quarantined.MustOnlyBeReferencedBy(quarantined);

        List<Selection> facades = boundary
            .Select(type => (Selection)new RefinedSelection(owner, new TypeNoun(type), Array.Empty<SelectionAdjective>()))
            .ToList();

        // sel.Except(F) . MustOnlyBeReferencedBy(sel, F...) — the formula holds whether the
        // facade types live inside or outside the quarantined selection (GRAMMAR §7).
        var union = new UnionSelection(owner, facades);
        Selection subject = quarantined.Except(union);
        return subject.MustOnlyBeReferencedBy(quarantined, facades.ToArray());
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
