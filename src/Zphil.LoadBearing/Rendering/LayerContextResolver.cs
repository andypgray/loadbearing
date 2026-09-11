using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Hosting;
using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     Works out which directory each layer's local-rules card belongs in. A layer earns a card when at
///     least one Enforce or Migrate rule has that layer as its subject: narrowing the layer with
///     adjectives or <c>Except</c> still counts, and so does naming it as one cell of a family, but a
///     union of selections does not, having no single home directory, so a union's rules stay in the
///     root block alone. Rules a scope produced are left out, their story belonging to the scope's own
///     card.
/// </summary>
/// <remarks>
///     A card lands in the deepest directory holding every file that declares one of the layer's types,
///     so it covers the layer and as little else as it can; a layer whose types are spread across the
///     solution therefore lands high up. A layer that matches no type in the solution comes back with a
///     null directory and the reason, for the caller to report or ignore. A purpose alone earns no card:
///     a card exists to carry rules.
/// </remarks>
// Scope children are excluded by their payload rather than their posture: a quarantined layer's
// containment subject is layer-anchored, but its story belongs to the scope card and the two
// emission keys must not double-emit — and keying on the payload means a posture added later cannot
// slip through the filter. A caution over a layer would be excluded even without it: its tripwire
// carries no constraint to read a subject from, so it anchors nothing.
public static class LayerContextResolver
{
    /// <summary>
    ///     Resolves a placement for every layer that has rules of its own, in the order the spec declares
    ///     the layers. Ask <see cref="HasAnchoredLayers" /> first to learn whether extracting
    ///     <paramref name="codebase" /> is worth the cost at all.
    /// </summary>
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
                    DirectoryPlacement.NoTypesSkipReason("layer", layer.Name))
                : new LayerPlacement(layer.Name, layer.Purpose, anchored, directory, null));
        }

        return placements;
    }

    /// <summary>
    ///     Whether any layer has at least one Enforce or Migrate rule of its own. Answered from the model
    ///     alone, so it is the question to ask before extracting a codebase to resolve placements against.
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
