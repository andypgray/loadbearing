namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     One rendered context card and where it goes: a layer card or a scope card, plus either the
///     directory whose <c>AGENTS.md</c> receives it or, when the layer or scope matched no type in the
///     solution, a null path and the reason it was skipped.
/// </summary>
// The one shape every consumer of scoped context reads, so `render` and the `context` verb's path
// lookup see the same card kinds in the same order, and an unplaceable card is data in both rather
// than a warning in one and a silent drop in the next.
public sealed class ContextCard
{
    internal ContextCard(string? directoryPath, string body, string? skipReason)
    {
        DirectoryPath = directoryPath;
        Body = body;
        SkipReason = skipReason;
    }

    /// <summary>
    ///     Gets the directory whose <c>AGENTS.md</c> receives <see cref="Body" />, or null when nothing placed the
    ///     card. Then <see cref="SkipReason" /> says why.
    /// </summary>
    public string? DirectoryPath { get; }

    /// <summary>
    ///     Gets the rendered card text. It carries no provenance line of its own: a file gets one line above all the
    ///     cards it holds.
    /// </summary>
    public string Body { get; }

    /// <summary>
    ///     Gets why the card was not placed, set exactly when <see cref="DirectoryPath" /> is null.
    /// </summary>
    public string? SkipReason { get; }
}
