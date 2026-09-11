using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Hosting;
using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     Places each scope's directory context file. For every card-bearing rule it evaluates the raw
///     scoped selection in <see cref="SelectionPosition.Subject" /> position (so it
///     ranges over solution-declared types), collects those types' declaration-site file paths, and
///     picks their <em>deepest common ancestor directory</em> — the directory whose <c>AGENTS.md</c>
///     receives the scope card.
/// </summary>
/// <remarks>
///     A scope that matches no types resolves to a null directory with a skip reason. This is the one
///     placement concern that needs the codebase, so it stays beside the internal
///     <see cref="SelectionEvaluator" /> and returns a public result.
/// </remarks>
public static class ScopedContextResolver
{
    /// <summary>Resolves a placement for every scope in the model, in model order.</summary>
    public static IReadOnlyList<ScopePlacement> Resolve(ArchitectureModel model, CodebaseModel codebase)
    {
        Guard.NotNull(model, nameof(model));
        Guard.NotNull(codebase, nameof(codebase));

        return Resolve(model, new SelectionEvaluator(codebase));
    }

    // The shared-evaluator entry point, the twin of LayerContextResolver's: a caller resolving both
    // emission keys for one codebase hands one evaluator to both, rather than each materializing the
    // solution-declared type list for itself.
    internal static IReadOnlyList<ScopePlacement> Resolve(ArchitectureModel model, SelectionEvaluator evaluator)
    {
        var placements = new List<ScopePlacement>();

        foreach (ArchRule rule in model.Rules)
        {
            if (rule.Scope is not { Scoped: { } scoped } scope || !IsCardBearing(rule, scope)) continue;

            string? directory = DirectoryPlacement.ResolveDirectory(evaluator, scoped);

            placements.Add(directory is null
                ? new ScopePlacement(scope.ScopeId, rule, null,
                    $"scope '{scope.ScopeId}' matched no types; no scoped context emitted")
                : new ScopePlacement(scope.ScopeId, rule, directory, null));
        }

        return placements;
    }

    // The one child of a scope its card is rendered from. A quarantine has two children carrying the same
    // scoped selection, and rendering both would place two cards on one directory, so the containment rule
    // is the card-bearer and its tripwire is passed over; a caution's tripwire is the only child there is.
    private static bool IsCardBearing(ArchRule rule, ScopeData scope)
    {
        return scope.Role == ScopeRole.Containment || rule.Posture == Posture.Caution;
    }
}
