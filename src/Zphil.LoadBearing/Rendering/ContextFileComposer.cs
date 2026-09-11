using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Hosting;
using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     Composes every <c>AGENTS.md</c> body one render writes: the root block in the solution directory,
///     a local-rules card for each layer that has rules of its own, and a card for each scope. Cards
///     that land in the same directory are merged into one body, layer cards first and scope cards
///     after, so no directory is written twice. Nothing here touches the file system or prints anything:
///     the files come back as text, and a card that could not be placed comes back as a warning for the
///     caller to report or ignore.
/// </summary>
// The single composition path: `render` splices what this returns and the card-drift gate compares
// against it, so the gate cannot drift from the command it gates. A gate that rebuilt the body its
// own way could only prove that two pieces of code agree with each other.
public static class ContextFileComposer
{
    /// <summary>The file name every managed context block is spliced into.</summary>
    public const string FileName = "AGENTS.md";

    /// <summary>
    ///     Composes the context files for <paramref name="model" />: the root block always, and the scoped
    ///     cards as well when a codebase is supplied.
    /// </summary>
    /// <param name="model">The model whose layers, rules and scopes are rendered.</param>
    /// <param name="codebase">
    ///     The extracted codebase the scoped cards are placed against, or null to compose the root block alone.
    /// </param>
    /// <param name="solutionDirectory">The directory whose <c>AGENTS.md</c> receives the root block.</param>
    /// <param name="specName">The spec assembly's name, written into each file's provenance line.</param>
    /// <remarks>
    ///     Extracting a codebase is the expensive half of a render, so whether to pay for one is the
    ///     caller's decision: ask <see cref="HasAnythingToPlace" /> first and skip the extraction when it
    ///     answers false.
    /// </remarks>
    public static ContextComposition Compose(
        ArchitectureModel model, CodebaseModel? codebase, string solutionDirectory, string specName)
    {
        Guard.NotNull(model, nameof(model));
        Guard.NotNullOrWhiteSpace(solutionDirectory, nameof(solutionDirectory));
        Guard.NotNullOrWhiteSpace(specName, nameof(specName));

        var units = new List<ContentUnit>
        {
            new(solutionDirectory, AgentContextRenderer.RootBlock(model, specName), true)
        };
        var warnings = new List<string>();

        if (codebase is not null) AddScopedUnits(model, codebase, units, warnings);

        return new ContextComposition(Group(units, specName), warnings);
    }

    /// <summary>
    ///     Whether this model places anything outside the solution directory: a scope of either posture, or
    ///     a layer with at least one rule of its own. Answered from the model alone, so it is the question
    ///     to ask before extracting a codebase for <see cref="Compose" /> or <see cref="Placements" />, both
    ///     of which have nothing to place against it when the answer is false.
    /// </summary>
    // The scope test reads the rule's scope payload rather than its posture, so a posture added to
    // Posture later cannot silently lose its card here.
    public static bool HasAnythingToPlace(ArchitectureModel model)
    {
        Guard.NotNull(model, nameof(model));

        return model.Rules.Any(rule => rule.Scope is not null)
               || LayerContextResolver.HasAnchoredLayers(model);
    }

    /// <summary>
    ///     Every scoped card this model places against <paramref name="codebase" />, rendered and paired
    ///     with the directory it belongs in: layer cards in declaration order, then scope cards in model
    ///     order. A card that could not be placed keeps its position in the list, carrying a null directory
    ///     and the reason it was skipped, rather than being dropped. Use it to ask which cards cover a given
    ///     path; <see cref="Compose" /> uses it to build the files.
    /// </summary>
    /// <param name="model">The model whose layers and scopes place the cards.</param>
    /// <param name="codebase">The extracted codebase the placements are resolved against.</param>
    // The composition decision itself, so Compose and any other consumer of scoped context (the
    // `context` verb's path lookup, say) agree on the card kinds, their order, and what an unplaceable
    // card means, instead of each walking the two resolvers and deciding again.
    public static IReadOnlyList<ContextCard> Placements(ArchitectureModel model, CodebaseModel codebase)
    {
        Guard.NotNull(model, nameof(model));
        Guard.NotNull(codebase, nameof(codebase));

        // One evaluator for both emission keys: it materializes the solution-declared type list and its
        // noun indexes in its constructor, and resolving the two keys separately built that twice.
        var evaluator = new SelectionEvaluator(codebase);
        var cards = new List<ContextCard>();

        foreach (LayerPlacement placement in LayerContextResolver.Resolve(model, evaluator))
            cards.Add(new ContextCard(
                placement.DirectoryPath,
                AgentContextRenderer.LayerCard(placement.LayerName, placement.Purpose, placement.Rules),
                placement.SkipReason));

        // Which card a scope gets is the posture's decision and only the posture's: the resolver already
        // picked the one card-bearing rule per scope, so this dispatch never has to ask which child it holds.
        foreach (ScopePlacement placement in ScopedContextResolver.Resolve(model, evaluator))
            cards.Add(new ContextCard(
                placement.DirectoryPath,
                placement.Rule.Posture == Posture.Caution
                    ? AgentContextRenderer.CautionCard(placement.Rule)
                    : AgentContextRenderer.ScopeCard(placement.Rule),
                placement.SkipReason));

        return cards;
    }

    // Layer cards (declaration order) ahead of scope cards (model order), so a directory hosting
    // both receives its layer unit first and Group merges them in that order.
    private static void AddScopedUnits(
        ArchitectureModel model, CodebaseModel codebase, List<ContentUnit> units, List<string> warnings)
    {
        foreach (ContextCard card in Placements(model, codebase))
        {
            if (card.DirectoryPath is null)
            {
                warnings.Add(card.SkipReason!);
                continue;
            }

            units.Add(new ContentUnit(card.DirectoryPath, card.Body, false));
        }
    }

    // Groups content units by target directory (first-seen order, so the root file comes first) and
    // assembles each file's one managed block — one provenance line, then the units in placement order.
    // A group that includes the root uses the root block's own provenance; a scoped-only file gets the
    // provenance line prepended.
    private static IReadOnlyList<ContextFile> Group(IReadOnlyList<ContentUnit> units, string specName)
    {
        var groups = new List<FileGroup>();
        foreach (ContentUnit unit in units)
        {
            string key = Path.GetFullPath(unit.Directory);
            // Per-OS so two case-spellings of one directory merge into a single AGENTS.md on
            // case-insensitive file systems (Windows/macOS) — Ordinal would splice the file twice, the
            // second clobbering the first — while staying distinct on Linux.
            FileGroup? group = groups.FirstOrDefault(candidate => string.Equals(candidate.Key, key, PathComparison.Comparison));
            if (group is null)
            {
                group = new FileGroup(key, unit.Directory);
                groups.Add(group);
            }

            group.Bodies.Add(unit.Body);
            group.HasRoot |= unit.IsRoot;
        }

        return groups.Select(group => new ContextFile(
            Path.Combine(group.Directory, FileName),
            group.HasRoot
                ? string.Join("\n\n", group.Bodies)
                : AgentContextRenderer.ProvenanceLine(specName) + "\n\n" + string.Join("\n\n", group.Bodies))).ToList();
    }

    private sealed class ContentUnit(string directory, string body, bool isRoot)
    {
        public string Directory { get; } = directory;
        public string Body { get; } = body;
        public bool IsRoot { get; } = isRoot;
    }

    private sealed class FileGroup(string key, string directory)
    {
        public string Key { get; } = key;
        public string Directory { get; } = directory;
        public List<string> Bodies { get; } = [];
        public bool HasRoot { get; set; }
    }
}
