using Zphil.LoadBearing.Baselines;

namespace Zphil.LoadBearing.Checking;

/// <summary>
///     One rule's evaluation outcome: its <see cref="RuleStatus" />, the <em>red</em>
///     <see cref="Violations" /> (ordered ordinal by source/subject then target FullName), any
///     <see cref="Warnings" />, and a <see cref="SkipReason" /> for a skipped posture. For a Migrate
///     rule the ratchet also fills <see cref="Grandfathered" /> (baselined violations that pass),
///     <see cref="StaleBaselineEntries" /> (baseline entries no current violation matched), and
///     <see cref="BaselineCaptured" />. An inert-target warning leaves the rule
///     <see cref="RuleStatus.Passed" /> (GRAMMAR §4.1).
/// </summary>
public sealed class RuleResult
{
    internal RuleResult(
        ArchRule rule,
        RuleStatus status,
        IReadOnlyList<Violation> violations,
        IReadOnlyList<CheckWarning> warnings,
        string? skipReason,
        IReadOnlyList<Violation> grandfathered,
        int staleBaselineEntries,
        bool baselineCaptured,
        IReadOnlyList<BaselineEntry>? grandfatheredEntries = null)
    {
        Rule = rule;
        Status = status;
        Violations = violations;
        Warnings = warnings;
        SkipReason = skipReason;
        Grandfathered = grandfathered;
        StaleBaselineEntries = staleBaselineEntries;
        BaselineCaptured = baselineCaptured;
        GrandfatheredEntries = grandfatheredEntries ?? Array.Empty<BaselineEntry>();
    }

    /// <summary>The rule that was evaluated.</summary>
    public ArchRule Rule { get; }

    /// <summary>The evaluation status.</summary>
    public RuleStatus Status { get; }

    /// <summary>The red (failing) violations, ordered deterministically; empty unless <see cref="Status" /> is Failed.</summary>
    public IReadOnlyList<Violation> Violations { get; }

    /// <summary>The non-fatal warnings (e.g. an inert forbidden-set target).</summary>
    public IReadOnlyList<CheckWarning> Warnings { get; }

    /// <summary>Why the rule was skipped, or null when it was evaluated.</summary>
    public string? SkipReason { get; }

    /// <summary>
    ///     The grandfathered (baselined) violations — they pass, so they are kept separate from
    ///     <see cref="Violations" />. Empty for every non-Migrate rule.
    /// </summary>
    public IReadOnlyList<Violation> Grandfathered { get; }

    /// <summary>
    ///     The count of baseline entries no current violation matched — debt that was fixed and is now
    ///     awaiting <c>loadbearing baseline --accept-reductions</c>. Zero for non-Migrate rules.
    /// </summary>
    public int StaleBaselineEntries { get; }

    /// <summary>Whether a baseline section exists for this (Migrate) rule; false when uncaptured or non-Migrate.</summary>
    public bool BaselineCaptured { get; }

    /// <summary>
    ///     The stored baseline entries that grandfathered <see cref="Grandfathered" />, <b>index-aligned</b>
    ///     with it: <c>GrandfatheredEntries[i]</c> is the baseline entry — carrying its
    ///     <see cref="BaselineEntry.Because" /> attribution, if any — that blessed
    ///     <c>Grandfathered[i]</c>. The two lists always share length and order. Empty for every
    ///     non-ratcheted rule and whenever <see cref="Grandfathered" /> is empty.
    /// </summary>
    public IReadOnlyList<BaselineEntry> GrandfatheredEntries { get; }
}