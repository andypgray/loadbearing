using Zphil.LoadBearing.Rendering;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     The one line every file-writing verb reports a target on. <c>render</c> and <c>baseline</c> both
///     end each write with it, and both words plus the solution-relative path are pinned stdout — so the
///     format is stated once here rather than four times across the two runners, where a third verb that
///     writes a file would state it a fifth.
/// </summary>
internal static class WriteReport
{
    /// <summary>
    ///     The report line for <paramref name="path" />: <c>wrote</c> when the bytes changed,
    ///     <c>unchanged</c> when they did not, then the path relative to
    ///     <paramref name="solutionDirectory" /> so the report reads the same on any machine.
    /// </summary>
    internal static string Line(WriteOutcome outcome, string solutionDirectory, string path)
    {
        string label = outcome == WriteOutcome.Wrote ? "wrote" : "unchanged";
        return $"{label} {PathFormat.Relative(solutionDirectory, path)}";
    }
}
