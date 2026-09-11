namespace Zphil.LoadBearing.Roslyn.Diagnostics;

/// <summary>
///     Writes a composed multi-line block to a writer, one <c>WriteLine</c> per line.
/// </summary>
/// <remarks>
///     Every block this repo composes — <see cref="IncompleteModelGate" />'s refusals,
///     <see cref="NarrowedUniverseNotice" />'s stamps, a validation error list, a context card — is joined
///     with <c>\n</c>, because a composed block has to be one comparable value whatever machine built it.
///     Written whole, those embedded LFs would reach a CRLF console verbatim; split and written a line at a
///     time, the block adopts the writer's own newline instead.
/// </remarks>
internal static class LineBlocks
{
    /// <summary>Writes each LF-separated line of <paramref name="block" /> as its own output line.</summary>
    /// <param name="writer">The channel the block goes out on.</param>
    /// <param name="block">An LF-joined block. A single-line block writes as one line.</param>
    internal static void Write(TextWriter writer, string block)
    {
        foreach (string line in block.Split('\n')) writer.WriteLine(line);
    }

    /// <summary>
    ///     Writes <paramref name="stamp" /> as <see cref="Write" /> writes every block, then the blank line
    ///     that separates a stamp from the answer it scopes.
    /// </summary>
    /// <remarks>
    ///     The shape every stamp takes, whichever subject it is about — one owner, so a verb that scopes its
    ///     answer with a stamp cannot be the one that forgets the separator.
    /// </remarks>
    /// <param name="writer">The verb's stdout writer — the stamp scopes what follows it there.</param>
    /// <param name="stamp">One of the per-verb stamps.</param>
    internal static void WriteStamp(TextWriter writer, string stamp)
    {
        Write(writer, stamp);
        writer.WriteLine();
    }
}
