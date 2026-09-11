namespace Zphil.LoadBearing.Checking;

/// <summary>
///     What the ratchet found in a rule's captured baseline section beyond the violations it
///     grandfathered: entries no current violation matched (<see cref="Stale" />), matched edge entries
///     whose observed site count came in under the recorded one (<see cref="Shrunk" />), and matched edge
///     entries that record no count at all (<see cref="Uncounted" />). A struct so that
///     <see langword="default" /> is the zero triple — what an unratcheted rule reports, with no null to
///     thread through the checker.
/// </summary>
/// <remarks>
///     Arrives whole rather than as three ints in a row on the constructor that takes it, and that is a
///     correctness matter rather than a tidiness one: same-typed positionals in a row are a transposition
///     no compiler can catch. All three are required, for the same reason — a trailing default would let
///     a single argument mean whichever of them the caller had in mind.
/// </remarks>
/// <param name="stale">Captured entries no current violation matched — fixed debt awaiting acceptance.</param>
/// <param name="shrunk">Matched edge entries whose observed site count is below the recorded one.</param>
/// <param name="uncounted">Matched edge entries recording no site count — grandfathered at pair grain.</param>
internal readonly struct RatchetMeasure(int stale, int shrunk, int uncounted)
{
    /// <summary>Captured entries no current violation matched — fixed debt awaiting acceptance.</summary>
    public int Stale { get; } = stale;

    /// <summary>Matched edge entries whose observed site count is below the recorded one.</summary>
    public int Shrunk { get; } = shrunk;

    /// <summary>Matched edge entries recording no site count — grandfathered at pair grain.</summary>
    public int Uncounted { get; } = uncounted;
}
