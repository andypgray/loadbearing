using Zphil.LoadBearing.Hosting;

namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     Where a scope's directory context file lands: the scope ID, its card-bearing rule (the
///     <see cref="AgentContextRenderer.ScopeCard" />/<see cref="AgentContextRenderer.CautionCard" />
///     source), and either the resolved <see cref="DirectoryPath" /> — the deepest common ancestor of the
///     scoped types' declaration sites — or a null path with a <see cref="SkipReason" /> when the scope
///     matched no types.
/// </summary>
public sealed class ScopePlacement
{
    internal ScopePlacement(string scopeId, ArchRule rule, string? directoryPath, string? skipReason)
    {
        ScopeId = scopeId;
        Rule = rule;
        DirectoryPath = directoryPath;
        SkipReason = skipReason;
    }

    /// <summary>The originating scope ID (e.g. <c>legacy/billing</c>).</summary>
    public string ScopeId { get; }

    /// <summary>
    ///     The desugared rule the scope card is rendered from — a quarantine's containment law, or a
    ///     caution's tripwire, which is the only child a caution has.
    /// </summary>
    public ArchRule Rule { get; }

    /// <summary>
    ///     The directory whose <c>AGENTS.md</c> receives the scope card, or null when the scope
    ///     matched no solution types (then <see cref="SkipReason" /> explains the skip).
    /// </summary>
    public string? DirectoryPath { get; }

    /// <summary>The skip explanation (for a stderr warning) iff <see cref="DirectoryPath" /> is null.</summary>
    public string? SkipReason { get; }
}
