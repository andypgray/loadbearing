namespace Zphil.LoadBearing.Cli.Mcp.Infrastructure;

/// <summary>
///     Loads the embedded <c>server-instructions.md</c> resource for use as the MCP server's
///     <c>ServerInstructions</c> (surfaced to clients on <c>initialize</c>), optionally banner-prefixed with
///     the reason the server could not bind to a solution.
/// </summary>
internal static class ServerInstructions
{
    internal static readonly string Text = EmbeddedResourceText.Load("server-instructions.md");

    /// <summary>
    ///     The instructions to hand the client: <see cref="Text" /> unchanged when the server bound
    ///     normally, and otherwise a banner carrying <paramref name="bindingFailure" /> verbatim above it.
    /// </summary>
    /// <remarks>
    ///     A server launched with no solution argument whose walk-up finds nothing starts anyway — the
    ///     alternative is a process that dies during <c>initialize</c>, which reaches the client as "the
    ///     server failed to start" and nothing else. So the reason takes the two channels a client does
    ///     read: this banner, delivered once at the handshake, and every tool call's error result, which
    ///     carries the identical discovery message because the tools re-run discovery per call. The banner
    ///     is only ever prepended — the embedded resource is never edited, so a bound server and an unbound
    ///     one describe the same tools in the same words.
    /// </remarks>
    /// <param name="bindingFailure">The discovery refusal, or <see langword="null" /> when the bind succeeded.</param>
    internal static string For(string? bindingFailure)
    {
        if (bindingFailure is null) return Text;

        return "**This server is running but is not bound to a solution.** Solution discovery failed:\n\n"
               + bindingFailure
               + "\n\nEvery tool call below returns that same error until the solution is named: add it to this "
               + "server's `args` in the client config (after `mcp`), or set `LOADBEARING_SOLUTION_PATH` in its "
               + "`env`. Nothing else about this server changes.\n\n"
               + Text;
    }
}
