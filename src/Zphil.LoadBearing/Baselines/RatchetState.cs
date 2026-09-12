namespace Zphil.LoadBearing.Baselines;

/// <summary>
///     How a run's observation of one grandfathered violation stands against the
///     <see cref="BaselineEntry" /> that grandfathers it — the four-way call the ratchet makes
///     (GRAMMAR §4.3), plus the arm where there is nothing to compare. <see cref="BaselineRatchet" />
///     decides which one an entry is in.
/// </summary>
internal enum RatchetState
{
    /// <summary>
    ///     No measure to take: a subject entry, whose sites are declarations, or an edge the run saw with
    ///     no <c>file:line</c> evidence at all. Grandfathered at pair grain, and silent — there is nothing
    ///     a write could record and nothing a reader could act on.
    /// </summary>
    NotMeasured,

    /// <summary>
    ///     An edge entry carrying no count against sited evidence: it grandfathers its pair at any size.
    ///     The one arm a write can clear, which is why it is reported rather than silent.
    /// </summary>
    Uncounted,

    /// <summary>Fewer sites than the entry records — a reduction, which passes and awaits acceptance.</summary>
    Shrunk,

    /// <summary>Exactly as many sites as the entry records — the steady state, which says nothing.</summary>
    Held,

    /// <summary>
    ///     More sites than the entry records: new code in the old pattern, inside a pair the baseline
    ///     already blesses. The only arm that is red.
    /// </summary>
    Grown
}
