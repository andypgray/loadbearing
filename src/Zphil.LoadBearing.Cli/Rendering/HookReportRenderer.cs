using System.Text.Json;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     Renders a clean check run's report as the JSON a Claude Code <c>PostToolUse</c> hook injects into
///     the agent's transcript — the fourth render target over the same run, and the only one whose reader
///     is the agent rather than a human, a code-scanning service, or a client parsing
///     <see cref="JsonReportRenderer" />'s document.
/// </summary>
/// <remarks>
///     <para>
///         The channel exists because a hook that exits 0 cannot block and its plain stdout reaches nobody,
///         while <c>hookSpecificOutput.additionalContext</c> on that same exit-0 stdout arrives in the
///         transcript as a system message. That is exactly a warning's shape: a tripwire warning has no
///         verdict to change and everything to say, and until this it travelled through the wrapper into a
///         discarded variable.
///     </para>
///     <para>
///         It lives here, in the product, rather than in the wrappers, because the whole job is escaping a
///         multi-line report into a JSON string and neither POSIX <c>sh</c> nor Windows PowerShell 5.1 can
///         be trusted to do that twice identically. Serialization is the shared
///         <see cref="LoadBearingJson.Options" />, so this document and the other three cannot drift in
///         escaping.
///     </para>
///     <para>
///         Two records rather than a <c>*Dtos.cs</c> twin: this document is one object with one nested
///         object, and its whole wire shape fits beside the renderer that writes it.
///     </para>
/// </remarks>
internal static class HookReportRenderer
{
    /// <summary>The hook event this document answers — the only one whose additional context reaches the agent.</summary>
    private const string HookEventName = "PostToolUse";

    /// <summary>
    ///     The one-object document carrying <paramref name="report" /> as additional context, with no
    ///     trailing newline (the caller's <c>WriteLine</c> supplies it).
    /// </summary>
    public static string Document(string report)
    {
        var payload = new HookJson(new HookSpecificOutputJson(HookEventName, report));
        return JsonSerializer.Serialize(payload, LoadBearingJson.Context.HookJson);
    }
}

/// <summary>The root of the hook document: Claude Code reads one <c>hookSpecificOutput</c> object.</summary>
internal sealed record HookJson(HookSpecificOutputJson HookSpecificOutput);

/// <summary>
///     The hook payload: which event it answers, and the text Claude Code turns into a transcript system
///     message the agent reads.
/// </summary>
internal sealed record HookSpecificOutputJson(string HookEventName, string AdditionalContext);
