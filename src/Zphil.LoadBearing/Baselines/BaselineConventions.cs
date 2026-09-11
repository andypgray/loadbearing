using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Baselines;

/// <summary>
///     Where LoadBearing looks for a baseline file when a rule does not name one.
/// </summary>
public static class BaselineConventions
{
    /// <summary>
    ///     The baseline file a rule uses when it declares no <c>Baseline(path)</c> of its own:
    ///     <c>arch/baselines/{ruleId}.json</c>, each <c>/</c> in the rule ID becoming a directory, so
    ///     <c>legacy/no-direct-sql</c> is baselined in <c>arch/baselines/legacy/no-direct-sql.json</c>. A
    ///     quarantined scope reaches the same convention through its containment rule, whose ID is
    ///     <c>{scope-id}/containment</c>. The path is written with forward slashes and resolves against
    ///     the solution directory. A blank rule ID is refused.
    /// </summary>
    // Rule IDs match ^[a-z0-9-]+(/[a-z0-9-]+)*$ (GRAMMAR §4.4), so turning the separators into
    // directories always yields a filesystem-safe path.
    public static string DefaultPath(string ruleId)
    {
        return "arch/baselines/" + Guard.NotNullOrWhiteSpace(ruleId, nameof(ruleId)) + ".json";
    }
}
