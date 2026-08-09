using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Baselines;

/// <summary>
///     The conventional baseline locations (GRAMMAR §4.4). When a Migrate rule omits <c>.Baseline(path)</c>
///     the model is filled with <see cref="DefaultPath" /> at build time, so a rule's baseline path is
///     never null post-build.
/// </summary>
public static class BaselineConventions
{
    /// <summary>The default baseline path for a rule: <c>arch/baselines/{ruleId}.json</c>.</summary>
    /// <remarks>
    ///     The rule ID's <c>/</c> separators become subdirectories — IDs match
    ///     <c>^[a-z0-9-]+(/[a-z0-9-]+)*$</c>, so the result is always filesystem-safe. The path is
    ///     stored forward-slash in the model and is relative to the solution directory.
    /// </remarks>
    public static string DefaultPath(string ruleId)
    {
        return "arch/baselines/" + Guard.NotNullOrWhiteSpace(ruleId, nameof(ruleId)) + ".json";
    }
}
