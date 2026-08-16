namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     Writes a composed multi-line block to a writer, one <c>WriteLine</c> per line.
/// </summary>
/// <remarks>
///     Every block this repo composes — <see cref="IncompleteModelGate" />'s refusals,
///     <see cref="NarrowedUniverseNotice" />'s stamps, a validation error list, a context card — is joined
///     with <c>\n</c>, because a composed block has to be one comparable value whatever machine built it.
///     Written whole, those embedded LFs would reach a CRLF console verbatim; split and written a line at a
///     time, the block adopts the writer's own newline instead. Sited here because both composers live in
///     this project, and the CLI's writers reach it through <c>InternalsVisibleTo</c>.
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
}
