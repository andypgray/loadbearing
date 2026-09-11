using Zphil.LoadBearing.Hosting;

namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     Where a scope's context card goes: the scope's ID, the rule the card is rendered from, and either
///     the directory whose <c>AGENTS.md</c> receives it or a null path with the reason it was skipped.
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

    /// <summary>
    ///     Gets the scope's ID as the spec declares it, <c>legacy/billing</c> say.
    /// </summary>
    public string ScopeId { get; }

    /// <summary>
    ///     Gets the rule the card is rendered from: a quarantine's containment rule, or a caution's tripwire, which is
    ///     the only rule a caution has.
    /// </summary>
    public ArchRule Rule { get; }

    /// <summary>
    ///     Gets the directory whose <c>AGENTS.md</c> receives the scope card, which is the deepest one holding every
    ///     file that declares a scoped type, or null when the scope matched no type in the solution. Then
    ///     <see cref="SkipReason" /> says so.
    /// </summary>
    public string? DirectoryPath { get; }

    /// <summary>
    ///     Gets why no directory was resolved, set exactly when <see cref="DirectoryPath" /> is null. A host with
    ///     somewhere to put it prints it as a warning.
    /// </summary>
    public string? SkipReason { get; }
}
