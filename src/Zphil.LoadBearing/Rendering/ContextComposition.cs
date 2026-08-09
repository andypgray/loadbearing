namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     The whole result of composing a solution's agent-context files: the
///     <see cref="Files" /> to write, and the <see cref="Warnings" /> raised by placements that
///     resolved to no directory (a layer or scope that matched no solution types).
/// </summary>
/// <remarks>
///     The warnings are returned rather than written so the composer stays free of any output channel.
/// </remarks>
public sealed class ContextComposition
{
    internal ContextComposition(IReadOnlyList<ContextFile> files, IReadOnlyList<string> warnings)
    {
        Files = files;
        Warnings = warnings;
    }

    /// <summary>The composed files, root first and then scoped cards in placement order.</summary>
    public IReadOnlyList<ContextFile> Files { get; }

    /// <summary>Skip explanations for placements that resolved to no directory, in placement order.</summary>
    public IReadOnlyList<string> Warnings { get; }
}
