using Zphil.LoadBearing.Roslyn.MsBuild;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     The shape every refusal, caveat and stamp in this repo states its evidence in: a lede, the evidence
///     two-space-indented beneath it, then what to do about it.
/// </summary>
/// <remarks>
///     <para>
///         <b>Only the literals ever varied.</b> <see cref="IncompleteModelGate" /> and
///         <see cref="NarrowedUniverseNotice" /> both defend their per-surface wording as separate messages
///         rather than one parameterized string — each names what <em>that</em> surface cannot do — but the
///         assembly beneath the wording was never the part that differed, and the spec-resolution refusal
///         reaches for the same shape a third time.
///     </para>
///     <para>
///         <b>The block is LF-joined, never platform-joined.</b> A composed block is one comparable value
///         whatever machine built it; <see cref="LineBlocks" /> is what turns it back into console lines.
///     </para>
/// </remarks>
internal static class EvidenceBlock
{
    /// <summary>
    ///     The composed block: <paramref name="lede" />, then <paramref name="evidence" /> indented two
    ///     spaces (with the elision count when <paramref name="quoteCap" /> bites), then — inside that
    ///     indented run, never after the tail — the MSBuild selection note, then <paramref name="tail" />.
    /// </summary>
    /// <param name="lede">The sentence the evidence hangs from, ending in a colon.</param>
    /// <param name="evidence">The paths or diagnostics being named, one per line.</param>
    /// <param name="tail">
    ///     The closing sentence — the remedy, or the reading being denied. Omitted where the lede already
    ///     said everything (an opted-in run has nothing to ask for).
    /// </param>
    /// <param name="withSelectionNote">
    ///     Whether to name the MSBuild instance this run selected. On for a refusal carrying its own
    ///     evidence — an MCP surface has no stderr echo above to read it from.
    /// </param>
    /// <param name="quoteCap">
    ///     How many entries to quote before counting the rest. A refusal nobody reads to the end names
    ///     nothing; omitted where the evidence is a project list the reader has to act on entry by entry.
    /// </param>
    internal static string Compose(
        string lede,
        IReadOnlyList<string> evidence,
        string? tail = null,
        bool withSelectionNote = false,
        int? quoteCap = null)
    {
        int quoted = quoteCap ?? evidence.Count;

        var lines = new List<string> { lede };
        lines.AddRange(evidence.Take(quoted).Select(entry => "  " + entry));
        if (evidence.Count > quoted) lines.Add($"  ... and {evidence.Count - quoted} more.");
        if (withSelectionNote) lines.Add("  " + MsBuildBootstrap.SelectionNote());
        if (tail is not null) lines.Add(tail);

        return string.Join("\n", lines);
    }
}
