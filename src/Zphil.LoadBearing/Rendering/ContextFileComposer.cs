using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     Composes every agent-context file one render writes, from the model plus (optionally) the
///     codebase: the root block in the solution directory, each anchored layer's local-rules card, and
///     each quarantined scope's card, grouped by target directory so a directory receiving several
///     cards gets one merged managed block (layer cards before quarantine cards). This is the single
///     composition path — <c>render</c> splices what it returns and the card-drift gate compares
///     against it, so the gate cannot drift from the command it gates. Pure: no file system access and
///     no output channel; skip warnings come back as data.
/// </summary>
public static class ContextFileComposer
{
    /// <summary>The file name every managed context block is spliced into.</summary>
    public const string FileName = "AGENTS.md";

    /// <summary>
    ///     Composes the context files for <paramref name="model" />. Pass the extracted
    ///     <paramref name="codebase" /> to place scoped cards, or null to compose the root block alone —
    ///     the caller owns the decision, because extraction is the expensive half and only
    ///     <see cref="LayerContextResolver.HasAnchoredLayers" /> plus a quarantine scan can say whether
    ///     it is worth paying.
    /// </summary>
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

    // Layer cards (declaration order) ahead of quarantine cards (model order), so a directory hosting
    // both receives its layer unit first and Group merges them in that order.
    private static void AddScopedUnits(
        ArchitectureModel model, CodebaseModel codebase, List<ContentUnit> units, List<string> warnings)
    {
        foreach (LayerPlacement placement in LayerContextResolver.Resolve(model, codebase))
        {
            if (placement.DirectoryPath is null)
            {
                warnings.Add(placement.SkipReason!);
                continue;
            }

            units.Add(new ContentUnit(
                placement.DirectoryPath, AgentContextRenderer.LayerCard(placement.LayerName, placement.Rules), false));
        }

        foreach (ScopePlacement placement in ScopedContextResolver.Resolve(model, codebase))
        {
            if (placement.DirectoryPath is null)
            {
                warnings.Add(placement.SkipReason!);
                continue;
            }

            units.Add(new ContentUnit(
                placement.DirectoryPath, AgentContextRenderer.ScopeCard(placement.ContainmentRule), false));
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
