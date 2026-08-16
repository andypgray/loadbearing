using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Hosting;
using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     Composes every agent-context file one render writes, from the model plus (optionally) the
///     codebase: the root block in the solution directory, each anchored layer's local-rules card, and
///     each quarantined scope's card, grouped by target directory so a directory receiving several
///     cards gets one merged managed block (layer cards before quarantine cards).
/// </summary>
/// <remarks>
///     This is the single composition path — <c>render</c> splices what it returns and the card-drift
///     gate compares against it, so the gate cannot drift from the command it gates. Pure: no file
///     system access and no output channel; skip warnings come back as data.
/// </remarks>
public static class ContextFileComposer
{
    /// <summary>The file name every managed context block is spliced into.</summary>
    public const string FileName = "AGENTS.md";

    /// <summary>Composes the context files for <paramref name="model" />.</summary>
    /// <param name="model">The reified spec whose layers, rules and quarantined scopes are rendered.</param>
    /// <param name="codebase">
    ///     The extracted codebase the scoped cards are placed against, or null to compose the root block alone.
    /// </param>
    /// <param name="solutionDirectory">The directory whose <c>AGENTS.md</c> receives the root block.</param>
    /// <param name="specName">The spec assembly name written into each file's provenance line.</param>
    /// <remarks>
    ///     Whether to pass a <paramref name="codebase" /> is the caller's decision, because extraction is
    ///     the expensive half and only <see cref="HasAnythingToPlace" /> can say whether it is worth paying.
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
    ///     Whether this model places anything scoped at all — a quarantined scope, or a layer carrying
    ///     anchored rules.
    /// </summary>
    /// <remarks>
    ///     The cost gate to consult before extracting: extraction is the expensive half, and with nothing
    ///     scoped to place there is nothing for it to place. Pure over the model, so it is answered before
    ///     a codebase exists.
    /// </remarks>
    public static bool HasAnythingToPlace(ArchitectureModel model)
    {
        Guard.NotNull(model, nameof(model));

        return model.Rules.Any(rule => rule.Posture == Posture.Quarantine)
               || LayerContextResolver.HasAnchoredLayers(model);
    }

    /// <summary>
    ///     Every scoped card this model places against <paramref name="codebase" />, rendered and paired
    ///     with its directory: layer local-rules cards in declaration order ahead of quarantine cards in
    ///     model order, and an unplaceable card carried as a null directory with its skip reason rather
    ///     than dropped.
    /// </summary>
    /// <param name="model">The reified spec whose layers and quarantined scopes place the cards.</param>
    /// <param name="codebase">The extracted codebase the placements are resolved against.</param>
    /// <remarks>
    ///     This is the composition decision itself, so <see cref="Compose" /> and any other consumer of
    ///     scoped context — a lookup that filters the cards by which one covers a path, say — agree on the
    ///     card kinds, their order, and what an unplaceable card means, instead of each walking the two
    ///     resolvers and deciding again.
    /// </remarks>
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
                AgentContextRenderer.LayerCard(placement.LayerName, placement.Rules),
                placement.SkipReason));

        foreach (ScopePlacement placement in ScopedContextResolver.Resolve(model, evaluator))
            cards.Add(new ContextCard(
                placement.DirectoryPath,
                AgentContextRenderer.ScopeCard(placement.ContainmentRule),
                placement.SkipReason));

        return cards;
    }

    // Layer cards (declaration order) ahead of quarantine cards (model order), so a directory hosting
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
