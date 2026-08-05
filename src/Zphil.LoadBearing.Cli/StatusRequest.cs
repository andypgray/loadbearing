namespace Zphil.LoadBearing.Cli;

/// <summary>The parsed inputs for <c>loadbearing status</c> — the burndown report (human or <c>--json</c>).</summary>
/// <param name="NoCache">Whether to bypass the persisted extraction cache entirely (no read, no write).</param>
/// <param name="Binlog">The explicit <c>--binlog</c> to replay instead of a design-time build, or null to auto-select.</param>
/// <param name="AllowWorkspaceDiagnostics">
///     Whether to report the burndown from the partial model when a project fails to load. Default
///     (<c>false</c>): the burndown still renders, then the run exits 2 — unloaded projects declare no types,
///     so every count reads low and a ratchet read against them looks like progress. Keys on exactly what
///     <c>check</c> keys on (<see cref="IncompleteModelGate" />).
/// </param>
internal sealed record StatusRequest(
    string? Solution,
    string? Spec,
    bool Json,
    string WorkingDirectory,
    bool NoCache,
    string? Binlog,
    bool AllowWorkspaceDiagnostics);
