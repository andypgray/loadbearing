namespace Zphil.LoadBearing.Tests.Rendering;

/// <summary>
///     What a rendered Mermaid block actually draws, for the two diagram suites that assert on the
///     drawing rather than on the frame around it.
/// </summary>
internal static class MermaidBlock
{
    /// <summary>
    ///     The diagram's node, edge and legend lines, unindented: everything between the accDescr
    ///     directive and the closing fence, so a test asserts on the drawing rather than re-pinning the
    ///     frame each time.
    /// </summary>
    public static IReadOnlyList<string> Diagram(string block)
    {
        var lines = block.Split('\n')
            .ToList();
        int start = lines.FindIndex(line => line.Contains("accDescr:", StringComparison.Ordinal)) + 2;
        int end = lines.FindLastIndex(line => line == "```");

        return lines.GetRange(start, end - start)
            .Select(line => line.Trim())
            .ToList();
    }
}
