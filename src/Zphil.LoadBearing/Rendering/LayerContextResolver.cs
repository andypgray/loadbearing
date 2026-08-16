using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     Places each declared layer's "local rules" context card — the second, additive
///     emission key beside quarantined scopes. A layer earns a card iff at least one Enforce or Migrate
///     rule is <em>anchored</em> on it: the rule's subject <see cref="Selection" /> has that layer's
///     <see cref="Model.LayerNoun" /> as its noun head (adjectives and <c>Except</c> refinements keep
///     the noun head, so a refined subject still anchors).
/// </summary>
/// <remarks>
///     Quarantine-posture rules are excluded — a quarantined layer's desugared containment subject is
///     layer-anchored, but its story belongs to the quarantine card, and the two keys must not
///     double-emit. The card lands in the deepest common ancestor directory of the layer's matched types,
///     shared with <see cref="ScopedContextResolver" /> through <see cref="DirectoryPlacement" />. Like
///     scoped placement, this is the one concern that needs the codebase, so it stays beside the internal
///     <see cref="SelectionEvaluator" /> and returns a public result.
/// </remarks>
public static class LayerContextResolver
{
    /// <summary>Resolves a placement for every anchored layer in the model, in declaration order.</summary>
    public static IReadOnlyList<LayerPlacement> Resolve(ArchitectureModel model, CodebaseModel codebase)
    {
        Guard.NotNull(model, nameof(model));
        Guard.NotNull(codebase, nameof(codebase));

        return Resolve(model, new SelectionEvaluator(codebase));
    }

    // The shared-evaluator entry point. An evaluator materializes the solution-declared type list and its
    // noun indexes once, so a caller resolving both emission keys for one codebase builds one and hands
    // it to both resolvers rather than paying for that list twice.
    internal static IReadOnlyList<LayerPlacement> Resolve(ArchitectureModel model, SelectionEvaluator evaluator)
    {
        var placements = new List<LayerPlacement>();

        foreach (LayerDefinition layer in model.Layers)
        {
            List<ArchRule> anchored = AnchoredRules(model, layer).ToList();
            if (anchored.Count == 0) continue; // A layer no rule anchors on gets no placement at all.

            string? directory = DirectoryPlacement.ResolveDirectory(evaluator, BareLayer(anchored[0]));

            placements.Add(directory is null
                ? new LayerPlacement(layer.Name, anchored, null,
                    $"layer '{layer.Name}' matched no types; no scoped context emitted")
                : new LayerPlacement(layer.Name, anchored, directory, null));
        }

        return placements;
    }

    /// <summary>
    ///     Whether any declared layer has at least one anchored Enforce/Migrate rule — the cheap,
    ///     codebase-free gate to consult before paying the extraction cost.
    /// </summary>
    public static bool HasAnchoredLayers(ArchitectureModel model)
    {
        Guard.NotNull(model, nameof(model));

        return model.Layers.Any(layer => AnchoredRules(model, layer).Any());
    }

    // The Enforce/Migrate rules whose subject noun head is this layer, in model order. Quarantine rules
    // are excluded by the posture filter — a quarantined layer's containment story is the quarantine card's.
    private static IEnumerable<ArchRule> AnchoredRules(ArchitectureModel model, LayerDefinition layer)
    {
        return model.Rules.Where(rule =>
            rule.Posture is Posture.Enforce or Posture.Migrate && IsAnchoredOn(rule, layer));
    }

    // Anchored: the subject selection's noun head is a LayerNoun naming this layer. A refinement
    // (adjective / Except) produces a RefinedSelection that keeps the same noun, so a refined subject
    // still anchors on its layer. A union subject anchors nothing — it has no single home directory even
    // when a Layer is one of its operands, so its rule renders into the root block only (GRAMMAR §6); the
    // guard also keeps this off UnionSelection.Noun, which throws.
    private static bool IsAnchoredOn(ArchRule rule, LayerDefinition layer)
    {
        // Kept as separate conjuncts because the order is the guard: the UnionSelection test has to be
        // read before .Noun, which throws on a union. A merged pattern preserves that order but buries it.
        // ReSharper disable once MergeIntoPattern
        return rule.Constraint?.Subject is { } subject
               && subject is not UnionSelection
               && subject.Noun is LayerNoun noun
               && string.Equals(noun.Name, layer.Name, StringComparison.Ordinal);
    }

    // The whole layer as a bare Selection (its noun, no adjectives), so evaluation ranges over every
    // type in the layer rather than an anchored rule's possibly-refined subject. The LayerNoun is the
    // one IsAnchoredOn matched; its owning Arch is borrowed from the same subject (every selection in
    // one build shares the single Arch, and the evaluator never reads the owner).
    private static Selection BareLayer(ArchRule anchoredRule)
    {
        Selection subject = anchoredRule.Constraint!.Subject;
        return new Layer(subject.Owner, (LayerNoun)subject.Noun);
    }
}
