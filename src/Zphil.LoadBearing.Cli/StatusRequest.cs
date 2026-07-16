namespace Zphil.LoadBearing.Cli;

/// <summary>The parsed inputs for <c>loadbearing status</c> — the burndown report (human or <c>--json</c>).</summary>
/// <param name="NoCache">Whether to bypass the persisted extraction cache entirely (no read, no write).</param>
/// <param name="Binlog">The explicit <c>--binlog</c> to replay instead of a design-time build, or null to auto-select.</param>
internal sealed record StatusRequest(
    string? Solution,
    string? Spec,
    bool Json,
    string WorkingDirectory,
    bool NoCache,
    string? Binlog);