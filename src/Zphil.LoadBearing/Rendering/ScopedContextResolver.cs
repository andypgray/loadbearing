using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Hosting;
using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     Works out which directory each scope's card belongs in: the deepest directory holding every file
///     that declares one of the scoped types, so the card covers the scope and as little else as it can.
///     One card per scope, rendered from a quarantine's containment rule or a caution's tripwire, so a
///     quarantine never places two cards on one directory.
/// </summary>
/// <remarks>
///     Only types the solution declares are considered. A scope that matches none of them comes back
///     with a null directory and the reason, for the caller to report or ignore.
/// </remarks>
public static class ScopedContextResolver
{
    /// <summary>
    ///     Resolves a placement for every scope in <paramref name="model" />, in the order the spec declares
    ///     them.
    /// </summary>
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
            if (rule.Scope is not { } scope || !IsCardBearing(rule, scope)) continue;

            string? directory = DirectoryPlacement.ResolveDirectory(evaluator, scope.Scoped);

            placements.Add(directory is null
                ? new ScopePlacement(scope.ScopeId, rule, null,
                    DirectoryPlacement.NoTypesSkipReason("scope", scope.ScopeId))
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
