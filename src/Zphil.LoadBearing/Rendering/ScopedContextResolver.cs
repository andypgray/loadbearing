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

        return Resolve(model, new SelectionEvaluator(codebase));
    }

    // The shared-evaluator entry point, the twin of LayerContextResolver's: the composer resolves both
    // emission keys for one codebase, and one evaluator serves both rather than each materializing the
    // solution-declared type list for itself.
    internal static IReadOnlyList<ScopePlacement> Resolve(ArchitectureModel model, SelectionEvaluator evaluator)
    {
        var placements = new List<ScopePlacement>();

        foreach (ArchRule rule in model.Rules)
        {
            if (rule.Quarantine is not { Role: QuarantineRole.Containment, Quarantined: { } quarantined } quarantine) continue;

            string? directory = DirectoryPlacement.ResolveDirectory(evaluator, quarantined);

            placements.Add(directory is null
                ? new ScopePlacement(quarantine.ScopeId, rule, null,
                    $"scope '{quarantine.ScopeId}' matched no types; no scoped context emitted")
                : new ScopePlacement(quarantine.ScopeId, rule, directory, null));
        }

        return placements;
    }
}
