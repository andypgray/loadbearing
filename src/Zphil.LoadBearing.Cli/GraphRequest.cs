namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The parsed inputs for <c>loadbearing graph</c> — the pre-spec codebase survey (human or
///     <c>--json</c>). Deliberately no <c>Spec</c>: the survey is a property of the codebase, and derive
///     runs before any spec exists.
/// </summary>
/// <param name="NoCache">Whether to bypass the persisted extraction cache entirely (no read, no write).</param>
/// <param name="Binlog">The explicit <c>--binlog</c> to replay instead of a design-time build, or null to auto-select.</param>
internal sealed record GraphRequest(
    string? Solution,
    bool Json,
    string WorkingDirectory,
    bool NoCache,
    string? Binlog);