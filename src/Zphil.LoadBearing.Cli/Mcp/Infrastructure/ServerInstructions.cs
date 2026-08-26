using Zphil.LoadBearing.Roslyn.Hosting;

namespace Zphil.LoadBearing.Cli.Mcp.Infrastructure;

/// <summary>
///     Loads the embedded <c>server-instructions.md</c> resource for use as the MCP server's
///     <c>ServerInstructions</c> (surfaced to clients on <c>initialize</c>), optionally banner-prefixed with
///     the reason the server could not bind to a solution.
/// </summary>
internal static class ServerInstructions
{
    /// <summary>
    ///     The recovery an unbound session can actually perform: the same engine, driven from the CLI with
    ///     the solution named. Embedded verbatim in the banner and appended to every tool call's error
    ///     result, so the two channels a client reads cannot drift apart.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Deliberately silent about <c>args</c> and the environment variable, which the banner names in
    ///         the paragraph after this one. Those bind the <em>next</em> session: an agent inside this one
    ///         cannot edit the client config that launched it, and a remedy the reader cannot take reads as
    ///         no remedy at all — which is what a wave of field-test sessions did with it, driving the CLI
    ///         while five advertised tools sat unused.
    ///     </para>
    ///     <para>
    ///         Both spellings are named because this process cannot know which one resolves for whoever is
    ///         reading. The unbound server is by construction the registry-manifest server: that manifest has
    ///         nowhere to put a solution, and the <c>dnx</c> launch it prescribes runs the package
    ///         <em>without installing the tool</em>, so <c>loadbearing</c> is absent from exactly the session
    ///         this text was written for. The installed spelling still leads: it is right for every other
    ///         unbound shape, and a reader who has it tests it in one move.
    ///     </para>
    ///     <para>
    ///         Three things here are load-bearing and easy to undo by accident. <c>dotnet dnx</c>, never bare
    ///         <c>dnx</c>: on Windows that command is <c>dnx.cmd</c>, and a POSIX shell resolves a bare name
    ///         to <c>.exe</c> alone, so the short spelling is itself a command not found in the shell an agent
    ///         drives. <c>--yes</c>, which is a real option though it appears in no <c>--help</c> — without it
    ///         a cold machine can block on a trust prompt, and a blocked stdio session has no second channel
    ///         to say so on. And <see cref="ServerVersion.SemVer" /> rather than a floating id, because
    ///         "same engine, same verdicts" is a claim this sentence makes two clauses earlier; pinned, it
    ///         also resolves from the cache the server is already running out of.
    ///     </para>
    /// </remarks>
    internal static readonly string UnboundCallCoda =
        "The MCP tools stay unusable for this session; rebinding takes a client-config edit and a relaunch. "
        + "Use the CLI with the solution named instead — same engine, same verdicts: "
        + "`loadbearing graph <solution>` surveys the codebase, `loadbearing check <solution>` runs the rules. "
        + $"A registry-manifest session has no such command: `dotnet dnx Zphil.LoadBearing.Cli@{ServerVersion.SemVer} --yes -- graph <solution>`.";

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
    ///     carries the identical discovery message because the tools re-run discovery per call and gets
    ///     <see cref="UnboundCallCoda" /> appended by <c>GlobalCallToolFilter</c>. Both channels lead with
    ///     the recovery this session can perform and name the client-config edits it cannot only after it.
    ///     The banner is only ever prepended — the embedded resource is never edited, so a bound server and
    ///     an unbound one describe the same tools in the same words.
    /// </remarks>
    /// <param name="bindingFailure">The discovery refusal, or <see langword="null" /> when the bind succeeded.</param>
    internal static string For(string? bindingFailure)
    {
        if (bindingFailure is null) return Text;

        return "**This server is running but is not bound to a solution.** Solution discovery failed:\n\n"
               + bindingFailure
               + "\n\n"
               + UnboundCallCoda
               + "\n\nTo bind future sessions, add the solution to this server's `args` in the client config "
               + $"(after `mcp`), or set `{LoadBearingEnvVars.SolutionPath}` in its `env`. Nothing else about "
               + "this server changes.\n\n"
               + Text;
    }
}
