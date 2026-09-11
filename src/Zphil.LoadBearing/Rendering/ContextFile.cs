namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     One agent-context file a render produces: the <c>AGENTS.md</c> to write and the exact
///     managed-block body that belongs in it. The body is already merged, so a directory hosting both a
///     layer card and a scope card yields one file carrying both and there is nothing left to combine:
///     splice the body into the file, or compare it against what is there to detect drift.
/// </summary>
public sealed class ContextFile
{
    internal ContextFile(string path, string body)
    {
        Path = path;
        Body = body;
    }

    /// <summary>
    ///     Gets the absolute path of the <c>AGENTS.md</c> this body belongs in.
    /// </summary>
    public string Path { get; }

    /// <summary>
    ///     Gets the composed managed-block body: the provenance line first, then the cards. Its line endings are always
    ///     LF, and <c>ManagedBlock.Splice</c> converts them to the ones the target file already uses.
    /// </summary>
    public string Body { get; }
}
