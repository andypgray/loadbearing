namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     Where a frozen scope's directory context file lands: the scope ID, its containment rule
///     (the <see cref="AgentContextRenderer.ScopeCard" /> source), and either the resolved
///     <see cref="DirectoryPath" /> — the deepest common ancestor of the frozen types' declaration
///     sites — or a null path with a <see cref="SkipReason" /> when the scope matched no types.
/// </summary>
public sealed class ScopePlacement
{
    internal ScopePlacement(string scopeId, ArchRule containmentRule, string? directoryPath, string? skipReason)
    {
        ScopeId = scopeId;
        ContainmentRule = containmentRule;
        DirectoryPath = directoryPath;
        SkipReason = skipReason;
    }

    /// <summary>The originating scope ID (e.g. <c>legacy/billing</c>).</summary>
    public string ScopeId { get; }

    /// <summary>The desugared containment rule the scope card is rendered from.</summary>
    public ArchRule ContainmentRule { get; }

    /// <summary>
    ///     The directory whose <c>AGENTS.md</c> receives the scope card, or null when the scope
    ///     matched no solution types (then <see cref="SkipReason" /> explains the skip).
    /// </summary>
    public string? DirectoryPath { get; }

    /// <summary>The skip explanation (for a stderr warning) iff <see cref="DirectoryPath" /> is null.</summary>
    public string? SkipReason { get; }
}