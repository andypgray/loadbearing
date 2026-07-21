using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     Places each declared layer's "local rules" context card — the second, additive
///     emission key beside frozen scopes. A layer earns a card iff at least one Enforce or Migrate
///     rule is <em>anchored</em> on it: the rule's subject <see cref="Selection" /> has that layer's
///     <see cref="Model.LayerNoun" /> as its noun head (adjectives and <c>Except</c> refinements keep
///     the noun head, so a refined subject still anchors). Freeze-posture rules are excluded — a
///     frozen layer's desugared containment subject is layer-anchored, but its story belongs to the
///     freeze card, and the two keys must not double-emit. The card lands in the deepest common
///     ancestor directory of the layer's matched types, shared with
///     <see cref="ScopedContextResolver" /> through <see cref="DirectoryPlacement" />. Like scoped
///     placement, this is the one concern that needs the codebase, so it stays in Core to use the
///     internal <see cref="SelectionEvaluator" /> and the CLI sees only the public result.
/// </summary>
public static class LayerContextResolver
{
    /// <summary>Resolves a placement for every anchored layer in the model, in declaration order.</summary>
    public static IReadOnlyList<LayerPlacement> Resolve(ArchitectureModel model, CodebaseModel codebase)
    {
        Guard.NotNull(model, nameof(model));
        Guard.NotNull(codebase, nameof(codebase));

        var evaluator = new SelectionEvaluator(codebase);
        var placements = new List<LayerPlacement>();

        foreach (LayerDefinition layer in model.Layers)
        {
            var anchored = AnchoredRules(model, layer).ToList();
            if (anchored.Count == 0) continue; // A layer no rule anchors on gets no placement at all.

            var sites = evaluator.Evaluate(BareLayer(anchored[0]), SelectionPosition.Subject)
                .Where(type => !type.IsExternal)
                .SelectMany(type => type.DeclarationSites)
                .Select(site => site.FilePath)
                .Distinct()
                .ToList();

            placements.Add(sites.Count == 0
                ? new LayerPlacement(layer.Name, anchored, null,
                    $"layer '{layer.Name}' matched no types; no scoped context emitted")
                : new LayerPlacement(layer.Name, anchored, DirectoryPlacement.DeepestCommonDirectory(sites), null));
        }

        return placements;
    }

    /// <summary>
    ///     Whether any declared layer has at least one anchored Enforce/Migrate rule — the cheap,
    ///     codebase-free gate the render/context pipelines consult before paying the extraction cost.
    /// </summary>
    public static bool HasAnchoredLayers(ArchitectureModel model)
    {
        Guard.NotNull(model, nameof(model));

        return model.Layers.Any(layer => AnchoredRules(model, layer).Any());
    }

    // The Enforce/Migrate rules whose subject noun head is this layer, in model order. Freeze rules
    // are excluded by the posture filter — a frozen layer's containment story is the freeze card's.
    private static IEnumerable<ArchRule> AnchoredRules(ArchitectureModel model, LayerDefinition layer)
    {
        return model.Rules.Where(rule =>
            rule.Posture is Posture.Enforce or Posture.Migrate && IsAnchoredOn(rule, layer));
    }

    // Anchored: the subject selection's noun head is a LayerNoun naming this layer. A refinement
    // (adjective / Except) produces a RefinedSelection that keeps the same noun, so a refined subject
    // still anchors on its layer.
    private static bool IsAnchoredOn(ArchRule rule, LayerDefinition layer)
    {
        return rule.Constraint?.Subject.Noun is LayerNoun noun
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