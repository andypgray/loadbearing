using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Hosting;
using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     Places each declared layer's "local rules" context card — the second, additive
///     emission key beside scopes. A layer earns a card iff at least one Enforce or Migrate
///     rule is <em>anchored</em> on it: the rule's subject <see cref="Selection" /> has that layer's
///     <see cref="Model.LayerNoun" /> as its noun head (adjectives and <c>Except</c> refinements keep
///     the noun head, so a refined subject still anchors).
/// </summary>
/// <remarks>
///     Scope children are excluded by their payload rather than their posture — a quarantined layer's
///     desugared containment subject is layer-anchored, but its story belongs to the scope card, and the
///     two keys must not double-emit; keying on the payload means a posture added later cannot slip
///     through the filter. A caution over a layer would be excluded even without it: its tripwire carries
///     no constraint to read a subject from, so it anchors nothing. The card lands in the deepest common
///     ancestor directory of the layer's matched types,
///     shared with <see cref="ScopedContextResolver" /> through <see cref="DirectoryPlacement" />. Like
///     scoped placement, this is the one concern that needs the codebase, so it stays beside the internal
///     <see cref="SelectionEvaluator" /> and returns a public result. The layer's purpose rides on the
///     placement from its <see cref="LayerDefinition" />; a purpose alone earns no placement, because
///     anchoring is what a card is for.
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

            string? directory = DirectoryPlacement.ResolveDirectory(evaluator, BareLayer(anchored[0], layer));

            placements.Add(directory is null
                ? new LayerPlacement(layer.Name, layer.Purpose, anchored, null,
                    $"layer '{layer.Name}' matched no types; no scoped context emitted")
                : new LayerPlacement(layer.Name, layer.Purpose, anchored, directory, null));
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

    // The rules no scope desugared into whose subject noun head is this layer, in model order. A scope's
    // children carry a payload and are excluded on it — a scoped layer's story is the scope card's.
    private static IEnumerable<ArchRule> AnchoredRules(ArchitectureModel model, LayerDefinition layer)
    {
        return model.Rules.Where(rule => rule.Scope is null && IsAnchoredOn(rule, layer));
    }

    // Anchored: the subject selection's noun head names this layer — a LayerNoun that is it, or a family
    // of layers holding it as a cell (GRAMMAR §5.1, §6: a family rule places its bullet on every cell's
    // card, because the sentence names every cell and so reads correctly on each). A refinement
    // (adjective / Except) produces a RefinedSelection that keeps the same noun, so a refined subject —
    // family or plain — still anchors. A union subject anchors nothing: it has no single home directory
    // even when a Layer is one of its operands, so its rule renders into the root block only, and the
    // guard also keeps this off UnionSelection.Noun, which throws. The project form of a family anchors
    // nothing either, as a bare project noun does not.
    private static bool IsAnchoredOn(ArchRule rule, LayerDefinition layer)
    {
        // Kept as separate conjuncts because the order is the guard: the UnionSelection test has to be
        // read before .Noun, which throws on a union. A merged pattern preserves that order but buries it.
        // ReSharper disable once MergeIntoPattern
        return rule.Constraint?.Subject is { } subject
               && subject is not UnionSelection
               && MatchingCell(subject.Noun, layer.Name) is not null;
    }

    // The whole layer as a bare Selection (its noun, no adjectives), so evaluation ranges over every
    // type in the layer rather than an anchored rule's possibly-refined subject. For a family it is the
    // MATCHING cell rather than the subject's own head, which is what places one family rule's card at
    // each cell's own directory. Its owning Arch is borrowed from the same subject (every selection in
    // one build shares the single Arch, and the evaluator never reads the owner).
    private static Selection BareLayer(ArchRule anchoredRule, LayerDefinition layer)
    {
        // Both bangs and the third are IsAnchoredOn's: it already read this constraint's subject and
        // matched a cell on it, which no rule with a null constraint and no project-subject rule (whose
        // Subject is null, GRAMMAR §4.10) can do. Only an anchored rule reaches here.
        Selection subject = anchoredRule.Constraint!.Subject!;
        return new Layer(subject.Owner, MatchingCell(subject.Noun, layer.Name)!);
    }

    // The noun head naming this layer: the head itself, the family cell carrying the name, or null.
    private static LayerNoun? MatchingCell(SelectionNoun noun, string layerName)
    {
        if (noun is LayerNoun head)
            return string.Equals(head.Name, layerName, StringComparison.Ordinal) ? head : null;

        if (noun is not EachNoun { Layers: { } cells }) return null;

        return cells
            .Select(cell => (LayerNoun)cell.Noun)
            .FirstOrDefault(cell => string.Equals(cell.Name, layerName, StringComparison.Ordinal));
    }
}
