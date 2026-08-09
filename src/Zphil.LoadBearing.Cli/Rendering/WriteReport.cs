using Zphil.LoadBearing.Rendering;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     The one line every file-writing verb reports a target on. Both words and the solution-relative path
///     are pinned stdout, so the format is owned here rather than restated by each runner that writes a file.
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
