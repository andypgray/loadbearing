using Zphil.LoadBearing.Roslyn.Diagnostics;

namespace Zphil.LoadBearing.Cli.Verbs;

/// <summary>The parsed inputs for <c>loadbearing status</c> — the burndown report (human or <c>--json</c>).</summary>
/// <param name="Solution">The positional solution argument (a file, a directory, or null for cwd walk-up).</param>
/// <param name="Spec">The <c>--spec</c> value (a built DLL or a solution-member csproj), or null for convention.</param>
/// <param name="Json">Whether to emit the machine-readable JSON burndown document instead of human output.</param>
/// <param name="WorkingDirectory">The directory solution discovery walks up from.</param>
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
    bool AllowWorkspaceDiagnostics) : IReplayableRequest;
