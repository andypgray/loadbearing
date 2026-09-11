using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Hosting;

namespace Zphil.LoadBearing.Checking;

/// <summary>One rule's evaluation outcome.</summary>
/// <remarks>
///     <see cref="Violations" /> is ordered ordinal by source/subject then target FullName. An
///     inert-target warning leaves the rule <see cref="RuleStatus.Passed" /> (GRAMMAR §4.1).
/// </remarks>
public sealed class RuleResult
{
    private static readonly IReadOnlyDictionary<Violation, BaselineEntry> NothingGrown =
        new Dictionary<Violation, BaselineEntry>();

    // Both the subject coverage and the ratchet measure arrive whole rather than as loose ints, and that
    // is a correctness matter rather than a tidiness one: they land next to each other, and same-typed
    // positionals in a row are a transposition no compiler can catch. Folding the ratchet's three is the
    // cure this comment named when there was one of them. The properties below stay ints, because that is
    // what every renderer reads.
    internal RuleResult(
        ArchRule rule,
        RuleStatus status,
        IReadOnlyList<Violation> violations,
        IReadOnlyList<CheckWarning>? warnings = null,
        string? skipReason = null,
        IReadOnlyList<Violation>? grandfathered = null,
        RatchetMeasure ratchet = default,
        bool baselineCaptured = false,
        IReadOnlyList<BaselineEntry>? grandfatheredEntries = null,
        SubjectCoverage coverage = default,
        IReadOnlyDictionary<Violation, BaselineEntry>? grownEntries = null)
    {
        Rule = rule;
        Status = status;
        Violations = violations;
        Warnings = warnings ?? Array.Empty<CheckWarning>();
        SkipReason = skipReason;
        Grandfathered = grandfathered ?? Array.Empty<Violation>();
        StaleBaselineEntries = ratchet.Stale;
        ShrunkBaselineEntries = ratchet.Shrunk;
        UncountedBaselineEntries = ratchet.Uncounted;
        BaselineCaptured = baselineCaptured;
        GrandfatheredEntries = grandfatheredEntries ?? Array.Empty<BaselineEntry>();
        SubjectTypes = coverage.Types;
        SubjectGeneratedTypes = coverage.Generated;
        GrownEntries = grownEntries ?? NothingGrown;
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
    /// <remarks>
    ///     Counts identity, and only identity. A grandfathered pair that grew is matched and live, so it
    ///     is never stale — it is red, and a reduction sweep that treated it as fixed would delete the
    ///     very entry recording the debt.
    /// </remarks>
    public int StaleBaselineEntries { get; }

    /// <summary>
    ///     The count of matched edge entries whose observed site count came in <em>below</em> the one
    ///     they record — a real reduction, which passes and awaits
    ///     <c>loadbearing baseline --accept-reductions</c> to lower the recorded count.
    /// </summary>
    public int ShrunkBaselineEntries { get; }

    /// <summary>
    ///     The count of matched edge entries that record no site count, so they grandfather their pair at
    ///     any size. Subject entries never count here: they carry no measure at all, so nothing an author
    ///     could do would clear them from this total.
    /// </summary>
    public int UncountedBaselineEntries { get; }

    /// <summary>
    ///     The stored baseline entry behind each <em>grown</em> violation — a grandfathered pair carrying
    ///     more sites than its entry records, which is red (it is in <see cref="Violations" />) while
    ///     still naming the allowance it exceeded.
    /// </summary>
    /// <remarks>
    ///     Keyed by the violation object itself, and reference-keyed at that: <see cref="Violation" />
    ///     overrides neither <c>Equals</c> nor <c>GetHashCode</c>, so the default comparer is reference
    ///     identity, which is what a lookup from a rendered violation needs. A dictionary rather than a
    ///     list index-aligned with <see cref="Violations" />, because both render loops walk the
    ///     violations without carrying an index — and only some of them are grown.
    /// </remarks>
    public IReadOnlyDictionary<Violation, BaselineEntry> GrownEntries { get; }

    /// <summary>The count of grown entries — the size of <see cref="GrownEntries" />.</summary>
    public int GrownBaselineEntries => GrownEntries.Count;

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
    ///     How many types the rule's subject actually materialized to — a member-subject rule reports the
    ///     members' declaring types, so this and <see cref="SubjectGeneratedTypes" /> share a unit. Zero for
    ///     a rule that reached no subject at all: errored, skipped, tripwire, or empty-subject.
    /// </summary>
    public int SubjectTypes { get; }

    /// <summary>
    ///     How many of <see cref="SubjectTypes" /> a generator emitted
    ///     (<see cref="ITypeInfo.IsGenerated" />) — the rule's own answer to whether it is aimed at code
    ///     anyone can act on.
    /// </summary>
    /// <remarks>
    ///     Reported for passing and failing rules alike, and it carries no advice — this states a fact
    ///     about the subject and leaves the judgement where it belongs.
    /// </remarks>
    public int SubjectGeneratedTypes { get; }

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
