namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     One agent-context file a render produces: the target <c>AGENTS.md</c> path and the exact
///     managed-block body that belongs in it.
/// </summary>
/// <remarks>
///     The body is already merged — a directory hosting both a layer card and a quarantine card yields
///     one file with both cards in it — so a consumer only has to splice it or compare it against what
///     is committed.
/// </remarks>
public sealed class ContextFile
{
    internal ContextFile(string path, string body)
    {
        Path = path;
        Body = body;
    }

    /// <summary>The absolute path of the <c>AGENTS.md</c> this body belongs in.</summary>
    public string Path { get; }

    /// <summary>The composed managed-block body, LF-internal and provenance line first.</summary>
    public string Body { get; }
}
