using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Hosting;

namespace Zphil.LoadBearing.Checking;

/// <summary>
///     What checking one rule produced: its <see cref="Status" />, the violations that failed it, the
///     violations a baseline tolerated, any warnings, and the counts a burndown report reads. One of
///     these sits in <see cref="CheckReport.Results" /> for every rule the run covered.
///     <see cref="Violations" /> and <see cref="Grandfathered" /> are each ordered the same way on every
///     run, so two runs over unchanged code list them identically, and a warning leaves the rule
///     <see cref="RuleStatus.Passed" />.
/// </summary>
// Both lists come out of ArchChecker's one report comparer — ordinal by (source|subject FullName,
// target FullName|package name, member SymbolId, target|subject ProjectName) — so the red list and
// the grandfathered list agree, and a grown pair interleaves in report order rather than trailing.
public sealed class RuleResult
{
    private static readonly IReadOnlyDictionary<Violation, BaselineEntry> NothingGrown =
        new Dictionary<Violation, BaselineEntry>();

    // Both the subject coverage and the ratchet measure arrive whole rather than as loose ints, and that
    // is a correctness matter rather than a tidiness one: they land next to each other, and same-typed
    // positionals in a row are a transposition no compiler can catch. The properties below stay ints,
    // because that is what every renderer reads.
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
        GrandfatheredSiteCount = Grandfathered.Sum(violation => violation.Sites.Count);
        RatchetCells = RatchetCellSplit.Of(rule, Grandfathered);
        StaleBaselineEntries = ratchet.Stale;
        ShrunkBaselineEntries = ratchet.Shrunk;
        UncountedBaselineEntries = ratchet.Uncounted;
        BaselineCaptured = baselineCaptured;
        GrandfatheredEntries = grandfatheredEntries ?? Array.Empty<BaselineEntry>();
        SubjectTypes = coverage.Types;
        SubjectGeneratedTypes = coverage.Generated;
        GrownEntries = grownEntries ?? NothingGrown;
    }

    /// <summary>Gets the rule that was checked: its ID, posture, reason, fix and the sentence it renders as.</summary>
    public ArchRule Rule { get; }

    /// <summary>Gets the outcome: passed, failed, or skipped for want of a verdict.</summary>
    public RuleStatus Status { get; }

    /// <summary>
    ///     Gets the violations that failed the rule, in report order. Empty unless <see cref="Status" /> is
    ///     <see cref="RuleStatus.Failed" />: the ones a baseline tolerated are in
    ///     <see cref="Grandfathered" /> instead. A tolerated pair that has grown past the site count its
    ///     baseline entry records is here rather than there, and <see cref="GrownEntries" /> names the entry
    ///     it went past.
    /// </summary>
    public IReadOnlyList<Violation> Violations { get; }

    /// <summary>
    ///     Gets the warnings raised while checking the rule — a forbidden target that matched no types, or a
    ///     changed file inside a quarantined or a cautioned scope. A warning never fails the rule and never
    ///     changes the exit code; empty when there are none.
    /// </summary>
    public IReadOnlyList<CheckWarning> Warnings { get; }

    /// <summary>
    ///     Gets why the run reached no verdict for this rule — a scope's tripwire run with no
    ///     <c>--diff-base</c>, or a subject lying entirely in projects a <c>.slnf</c> solution filter left
    ///     unchecked. Written to be printed as it stands, and null unless <see cref="Status" /> is
    ///     <see cref="RuleStatus.Skipped" />.
    /// </summary>
    public string? SkipReason { get; }

    /// <summary>
    ///     Gets the violations the rule's captured baseline tolerates: each is reported, in report order,
    ///     and none of them fails the rule — the debt still to be paid down. Empty for a rule that takes no
    ///     baseline, and for one whose baseline has yet to be captured, since until it is every violation
    ///     fails.
    /// </summary>
    public IReadOnlyList<Violation> Grandfathered { get; }

    /// <summary>
    ///     Gets how many source sites <see cref="Grandfathered" />'s violations carry between them. One
    ///     violation covers every site of the same pair, so this is at least
    ///     <see cref="Grandfathered" />'s own count and is the finer measure of the same remaining debt;
    ///     it is available whether or not any baseline entry has a recorded count of its own.
    /// </summary>
    public int GrandfatheredSiteCount { get; }

    /// <summary>
    ///     Gets this rule's remaining tolerated debt grouped by the cell each violation falls in, or
    ///     null when there is nothing to group: the rule takes no baseline, its subject is not a family,
    ///     or nothing is left to burn down. <see cref="GrandfatheredSiteCount" /> says how much debt
    ///     remains and never where it sits, which on a family is the next question;
    ///     <c>loadbearing status</c> prints this under the rule's row.
    /// </summary>
    // Both status channels read this one property — the human row's sub-line and the burndown
    // document's cells array — so a reader cannot find the two disagreeing about the same run.
    public RatchetCellSplit? RatchetCells { get; }

    /// <summary>
    ///     Gets how many entries in the rule's captured baseline no current violation matched — debt that
    ///     has since been fixed, which <c>loadbearing baseline --accept-reductions</c> retires. Matching is
    ///     on identity alone, so a tolerated pair that has grown is matched and live rather than stale, even
    ///     though it now fails the rule. Zero for a rule that takes no baseline.
    /// </summary>
    public int StaleBaselineEntries { get; }

    /// <summary>
    ///     Gets how many matched baseline entries now cover fewer sites than they record — a real reduction.
    ///     Each still passes, and <c>loadbearing baseline --accept-reductions</c> lowers the recorded count.
    /// </summary>
    public int ShrunkBaselineEntries { get; }

    /// <summary>
    ///     Gets how many matched baseline entries record no site count, so each tolerates its pair however
    ///     many sites it grows to, until a capture records one. Entries that key a subject rather than a
    ///     pair never count here: they carry no site measure at all, so nothing an author could do would
    ///     clear them from this total.
    /// </summary>
    public int UncountedBaselineEntries { get; }

    /// <summary>
    ///     Gets, for each violation that has grown, the baseline entry it went past: a tolerated pair now
    ///     carrying more sites than its entry records, which fails the rule and so appears in
    ///     <see cref="Violations" /> rather than in <see cref="Grandfathered" />, while still naming the
    ///     allowance it exceeded. Look one up with the very instance taken from <see cref="Violations" /> —
    ///     the keys are those instances, compared by reference. Empty when nothing grew.
    /// </summary>
    // Reference-keyed because Violation overrides neither Equals nor GetHashCode, so the default comparer
    // is reference identity — which is what a lookup from a rendered violation needs. A dictionary rather
    // than a list index-aligned with Violations, because both render loops walk the violations without
    // carrying an index, and only some of them are grown.
    public IReadOnlyDictionary<Violation, BaselineEntry> GrownEntries { get; }

    /// <summary>
    ///     Gets how many violations have grown past their baseline entry — the size of <see cref="GrownEntries" />.
    /// </summary>
    public int GrownBaselineEntries => GrownEntries.Count;

    /// <summary>
    ///     Gets whether a baseline has been captured for this rule. False for a rule that takes no baseline,
    ///     and false while a rule that takes one has yet to be captured with the CLI's <c>baseline</c> verb
    ///     — until then every violation fails.
    /// </summary>
    public bool BaselineCaptured { get; }

    /// <summary>
    ///     Gets the baseline entries that tolerated <see cref="Grandfathered" />, one for one and in the
    ///     same order: the entry at index <c>i</c> is the one that blessed the violation at index <c>i</c>,
    ///     and carries the reason recorded with it (<see cref="BaselineEntry.Because" />) if it has one. The
    ///     two lists always share length and order. Empty whenever <see cref="Grandfathered" /> is.
    /// </summary>
    public IReadOnlyList<BaselineEntry> GrandfatheredEntries { get; }

    /// <summary>
    ///     Gets how many types the rule's subject actually matched. A rule over members counts the types
    ///     that declare them, so this and <see cref="SubjectGeneratedTypes" /> share a unit. Zero for a rule
    ///     that reached no subject at all: one that errored, one the run reached no verdict for, a scope's
    ///     tripwire, or a subject that matched nothing.
    /// </summary>
    public int SubjectTypes { get; }

    /// <summary>
    ///     Gets how many of <see cref="SubjectTypes" /> a generator emitted
    ///     (<see cref="ITypeInfo.IsGenerated" />) — the rule's own answer to whether it is aimed at code
    ///     anyone can act on. Reported for a rule that passed as readily as for one that failed.
    /// </summary>
    public int SubjectGeneratedTypes { get; }

    /// <summary>
    ///     Gets whether the rule's debt has burned to zero on a run that actually measured it, so its
    ///     posture could move from <see cref="Posture.Migrate" /> to <see cref="Posture.Enforce" />: a
    ///     captured baseline with nothing tolerated, nothing failing and nothing awaiting acceptance. The
    ///     CLI's <c>status</c> verb reports it as a suggestion. False for every other posture, false for
    ///     a rule the run reached no verdict for, and false where
    ///     <see cref="SelectionMatchedNothing" /> — burned to zero and never measured read alike in the
    ///     counts, and only one of them is progress.
    /// </summary>
    // The Skipped conjunct is load-bearing: a narrowing skip keeps BaselineCaptured truthful and zeroes
    // the counts, which is burned-to-zero's exact shape, so without it this would suggest enforcing a
    // rule whose subject a solution filter had merely erased. SelectionMatchedNothing is the same guard
    // against the same shape arrived at a different way: a rule whose target rotted passes with nothing
    // tolerated, nothing red and nothing stale, and enforcing it would pin a rule that measures nothing.
    public bool Promotable =>
        Rule.Posture == Posture.Migrate
        && Status != RuleStatus.Skipped
        && BaselineCaptured
        && Grandfathered.Count == 0
        && Violations.Count == 0
        && StaleBaselineEntries == 0
        && !SelectionMatchedNothing;

    /// <summary>
    ///     Gets whether this verdict rests on a selection that matched nothing, so the rule was reported
    ///     without ever being measured against the code: its subject matched no types
    ///     (<see cref="ViolationKind.EmptySubject" />, which fails it), or a forbidden target did
    ///     (<see cref="CheckWarningKind.InertTarget" />, which leaves it passing). Either way every count on
    ///     this result is a count of nothing, and a baseline entry no violation matched went unmatched for
    ///     want of a measurement rather than because the debt was paid: repair the rule's selection, and do
    ///     not accept the reduction.
    /// </summary>
    // The one predicate both rotted shapes answer, because every reader of it — the status row, the
    // burndown roll-up, the baseline verb's refusal — needs the same fact and would otherwise test the
    // violations for one shape and the warnings for the other, and miss whichever it forgot. Distinct from
    // ArchChecker's private SelectedNothing, which asks the narrower question of whether EVERY violation is
    // an empty subject: that one decides a skip, and one real finding disqualifies it.
    public bool SelectionMatchedNothing =>
        Violations.Any(violation => violation.Kind == ViolationKind.EmptySubject)
        || Warnings.Any(warning => warning.Kind == CheckWarningKind.InertTarget);
}
