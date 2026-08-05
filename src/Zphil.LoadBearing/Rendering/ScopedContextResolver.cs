using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     Places each quarantined scope's directory context file. For every containment rule it
///     evaluates the raw quarantined selection in <see cref="SelectionPosition.Subject" /> position (so it
///     ranges over solution-declared types), collects those types' declaration-site file paths, and
///     picks their <em>deepest common ancestor directory</em> — the directory whose <c>AGENTS.md</c>
///     receives the scope card. A scope that matches no types resolves to a null directory with a
///     skip reason. This is the one placement concern that needs the codebase; it stays in Core so it
///     can use the internal <see cref="SelectionEvaluator" />, and the CLI sees only the public result.
/// </summary>
public static class ScopedContextResolver
{
    /// <summary>Resolves a placement for every quarantined scope in the model, in model order.</summary>
    public static IReadOnlyList<ScopePlacement> Resolve(ArchitectureModel model, CodebaseModel codebase)
    {
        Guard.NotNull(model, nameof(model));
        Guard.NotNull(codebase, nameof(codebase));

        var evaluator = new SelectionEvaluator(codebase);
        var placements = new List<ScopePlacement>();

        foreach (ArchRule rule in model.Rules)
        {
            if (rule.Quarantine is not { Role: QuarantineRole.Containment, Quarantined: { } quarantined } quarantine) continue;

            var sites = evaluator.Evaluate(quarantined, SelectionPosition.Subject)
                .Where(type => !type.IsExternal)
                .SelectMany(type => type.DeclarationSites)
                .Select(site => site.FilePath)
                .Distinct()
                .ToList();

            placements.Add(sites.Count == 0
                ? new ScopePlacement(quarantine.ScopeId, rule, null,
                    $"scope '{quarantine.ScopeId}' matched no types; no scoped context emitted")
                : new ScopePlacement(quarantine.ScopeId, rule, DirectoryPlacement.DeepestCommonDirectory(sites), null));
        }

        return placements;
    }
}
