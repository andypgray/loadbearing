namespace Zphil.LoadBearing.Roslyn.Hosting;

/// <summary>
///     Every environment variable the product reads, named once. "What can an operator set" is a question
///     with one answer only if the names live together; spelled at each reader they are an inventory
///     nobody holds, and the prose that documents them (the MCP server instructions, the MSBuild
///     quarantine's dragons note) cites names it cannot check.
/// </summary>
/// <remarks>
///     The last two are not ours: they are the host's, read to fit in with it rather than to configure
///     anything of LoadBearing's.
/// </remarks>
internal static class LoadBearingEnvVars
{
    /// <summary>Explicit solution-file path; bypasses CWD-walk discovery.</summary>
    internal const string SolutionPath = "LOADBEARING_SOLUTION_PATH";

    /// <summary>Overrides the auto-selected Visual Studio install root for MSBuild registration.</summary>
    internal const string VsInstallPath = "LOADBEARING_VS_INSTALL_PATH";

    /// <summary>Overrides the persisted extraction cache's root directory; also the test-isolation knob.</summary>
    internal const string CacheDirectory = "LOADBEARING_CACHE_DIR";

    /// <summary>
    ///     Minutes of tool inactivity after which the MCP server exits; <c>0</c> is the documented opt-out,
    ///     and anything unparseable falls back to the default rather than disabling the watchdog.
    /// </summary>
    internal const string IdleTimeoutMinutes = "LOADBEARING_IDLE_TIMEOUT_MINUTES";

    /// <summary><c>true</c> opts the MCP server out of exiting when the process that launched it dies.</summary>
    internal const string DisableParentWatch = "LOADBEARING_DISABLE_PARENT_WATCH";

    /// <summary>
    ///     <c>true</c> makes the MCP server acquire the solution cold on every call instead of holding one
    ///     warm workspace for its lifetime.
    /// </summary>
    internal const string DisableWarmWorkspace = "LOADBEARING_DISABLE_WARM_WORKSPACE";

    /// <summary>The MCP server's minimum file-log level, in either Serilog or Microsoft level names.</summary>
    internal const string LogLevel = "LOADBEARING_LOG_LEVEL";

    /// <summary>
    ///     The launching agent session's id, set by Claude Code. Tagged onto every log line so concurrent
    ///     servers sharing one daily file can be told apart and correlated with the session that started them.
    /// </summary>
    internal const string ClaudeCodeSessionId = "CLAUDE_CODE_SESSION_ID";

    /// <summary>The MCP client's per-response token budget, read to size the response cap.</summary>
    internal const string MaxMcpOutputTokens = "MAX_MCP_OUTPUT_TOKENS";
}
