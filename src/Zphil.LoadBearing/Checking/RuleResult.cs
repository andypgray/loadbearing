using Zphil.LoadBearing.Baselines;

namespace Zphil.LoadBearing.Checking;

/// <summary>One rule's evaluation outcome.</summary>
/// <remarks>
///     <see cref="Violations" /> is ordered ordinal by source/subject then target FullName. An
///     inert-target warning leaves the rule <see cref="RuleStatus.Passed" /> (GRAMMAR §4.1).
/// </remarks>
public sealed class RuleResult
{
    internal RuleResult(
        ArchRule rule,
        RuleStatus status,
        IReadOnlyList<Violation> violations,
        IReadOnlyList<CheckWarning>? warnings = null,
        string? skipReason = null,
        IReadOnlyList<Violation>? grandfathered = null,
        int staleBaselineEntries = 0,
        bool baselineCaptured = false,
        IReadOnlyList<BaselineEntry>? grandfatheredEntries = null)
    {
        Rule = rule;
        Status = status;
        Violations = violations;
        Warnings = warnings ?? Array.Empty<CheckWarning>();
        SkipReason = skipReason;
        Grandfathered = grandfathered ?? Array.Empty<Violation>();
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

    /// <summary>
    ///     Why the run reached no verdict for this rule — a tripwire with no diff context, or a solution
    ///     filter that left its subject out of the checked universe — or null when it was evaluated.
    /// </summary>
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

    /// <summary>
    ///     Whether the Migrate ratchet has burned to zero on a rule this run actually measured, so the
    ///     posture can move to Enforce: a captured baseline with nothing grandfathered, nothing new, and
    ///     nothing awaiting acceptance.
    /// </summary>
    /// <remarks>
    ///     False for every other posture, deliberately — a burned-to-zero Quarantine containment reads plain,
    ///     because Quarantine→Migrate is a human decision and is never suggested. False too for a rule the run
    ///     reached no verdict on: a narrowing skip keeps <see cref="BaselineCaptured" /> truthful and zeroes
    ///     the counts, which is burned-to-zero's exact shape, so promoting on it would suggest enforcing a
    ///     rule whose subject a filter had merely erased.
    /// </remarks>
    public bool Promotable =>
        Rule.Posture == Posture.Migrate
        && Status != RuleStatus.Skipped
        && BaselineCaptured
        && Grandfathered.Count == 0
        && Violations.Count == 0
        && StaleBaselineEntries == 0;
}
