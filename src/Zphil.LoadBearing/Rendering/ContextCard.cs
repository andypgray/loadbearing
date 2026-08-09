namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     One scoped agent-context card and where it goes: a rendered layer or quarantine card, plus either
///     the <see cref="DirectoryPath" /> whose <c>AGENTS.md</c> receives it or — when the card's selection
///     matched no solution types — a null path with the <see cref="SkipReason" /> that says so.
/// </summary>
/// <remarks>
///     It is the unit <see cref="ContextFileComposer.Placements" /> hands back, so every consumer of
///     scoped context sees the same card kinds in the same order, and an unplaceable card is data in all
///     of them rather than a warning in one and a silent drop in the next.
/// </remarks>
public sealed class ContextCard
{
    internal ContextCard(string? directoryPath, string body, string? skipReason)
    {
        DirectoryPath = directoryPath;
        Body = body;
        SkipReason = skipReason;
    }

    /// <summary>
    ///     The directory whose <c>AGENTS.md</c> receives <see cref="Body" />, or null when nothing placed
    ///     the card (then <see cref="SkipReason" /> explains the skip).
    /// </summary>
    public string? DirectoryPath { get; }

    /// <summary>The card body, carrying no provenance line — that is a file-splice concern.</summary>
    public string Body { get; }

    /// <summary>The skip explanation iff <see cref="DirectoryPath" /> is null.</summary>
    public string? SkipReason { get; }
}
