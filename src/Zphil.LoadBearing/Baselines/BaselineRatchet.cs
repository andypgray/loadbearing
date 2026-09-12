namespace Zphil.LoadBearing.Baselines;

/// <summary>
///     The ratchet's four-way call on one baseline entry: how many sites a run observed under it, against
///     how many it grandfathers.
/// </summary>
/// <remarks>
///     <para>
///         One owner rather than a copy per caller, for the reason <c>Plurals</c> is one: the verdict path
///         and the write path both make this call — the checker to decide what is red and what a report
///         counts, <c>loadbearing baseline --accept-reductions</c> to decide what it may tighten — and two
///         hand-rolled copies of a four-way discrimination drift the first time either side is widened. They
///         already had: the verdict path keyed "uncounted" on the stored entry while the write path keyed it
///         on what the run observed, so an edge with no sited evidence was uncounted to one and nothing to
///         measure to the other. Here rather than beside the checker because the discrimination is entirely
///         about <see cref="BaselineEntry" /> state — <see cref="BaselineEntry.IsEdge" /> and
///         <see cref="BaselineEntry.SiteCount" /> — whose own prose carries the authoritative reading of all
///         four arms.
///     </para>
///     <para>
///         Reached from the CLI across <c>InternalsVisibleTo</c>, which is also what keeps the arms
///         un-rendered: <see cref="RatchetState" /> is a decision, and each caller still owns the sentence it
///         prints for one.
///     </para>
/// </remarks>
internal static class BaselineRatchet
{
    /// <summary>
    ///     Where <paramref name="stored" /> stands against the <paramref name="observedSites" /> distinct
    ///     <c>file:line</c> sites a run saw under its identity.
    /// </summary>
    /// <param name="stored">The entry as the baseline file holds it, not the identity synthesized from the run.</param>
    /// <param name="observedSites">How many sites the run observed; zero where it saw the pair with no evidence.</param>
    /// <remarks>
    ///     <para>
    ///         Two routes reach <see cref="RatchetState.NotMeasured" /> and both mean the same thing — the
    ///         entry grandfathers its pair at pair grain and no count can be recorded for it. A subject
    ///         entry's sites are declarations, so it never carries a measure; counting one as
    ///         <see cref="RatchetState.Uncounted" /> would leave a naming rule's whole section reading
    ///         "uncounted" with nothing an author could do about it. An edge with no sited evidence is the
    ///         same state arrived at from the run's end, and reading it any other way is what the two copies
    ///         of this call used to disagree about.
    ///     </para>
    ///     <para>
    ///         The zero test must stay ahead of the allowance compare. Behind it, an edge the run saw with no
    ///         evidence, against an entry recording sites, reads as <see cref="RatchetState.Shrunk" /> — and
    ///         the write path then lowers the count to zero, which
    ///         <see cref="BaselineEntry.WithSiteCount" /> refuses: an entry grandfathering nothing is a stale
    ///         entry, reported for acceptance rather than recorded.
    ///     </para>
    /// </remarks>
    internal static RatchetState Classify(BaselineEntry stored, int observedSites)
    {
        if (!stored.IsEdge) return RatchetState.NotMeasured;
        if (observedSites == 0) return RatchetState.NotMeasured;
        if (stored.SiteCount is not { } allowance) return RatchetState.Uncounted;
        if (observedSites > allowance) return RatchetState.Grown;

        return observedSites < allowance ? RatchetState.Shrunk : RatchetState.Held;
    }
}
