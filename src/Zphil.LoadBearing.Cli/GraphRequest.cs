namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The parsed inputs for <c>loadbearing graph</c> — the pre-spec codebase survey (human or
///     <c>--json</c>). Deliberately no <c>Spec</c>: the survey is a property of the codebase, and derive
///     runs before any spec exists.
/// </summary>
/// <param name="NoCache">Whether to bypass the persisted extraction cache entirely (no read, no write).</param>
/// <param name="Binlog">The explicit <c>--binlog</c> to replay instead of a design-time build, or null to auto-select.</param>
/// <param name="AllowWorkspaceDiagnostics">
///     Whether to survey the partial model when a project fails to load. Default (<c>false</c>): the survey
///     refuses before extraction with exit 2 (MCP: an error result), because a survey missing whole projects
///     is a wrong map, not a smaller one. Keys on exactly what <c>check</c> keys on
///     (<see cref="IncompleteModelGate" />).
/// </param>
internal sealed record GraphRequest(
    string? Solution,
    bool Json,
    string WorkingDirectory,
    bool NoCache,
    string? Binlog,
    bool AllowWorkspaceDiagnostics);