namespace Zphil.LoadBearing.Cli;

/// <summary>The parsed inputs to a <c>check</c> run — free of Roslyn types so it crosses the MSBuild gate.</summary>
/// <param name="Solution">The positional solution argument (a file, a directory, or null for cwd walk-up).</param>
/// <param name="Spec">The <c>--spec</c> value (a built DLL or a solution-member csproj), or null for convention.</param>
/// <param name="Json">Whether to emit the machine-readable JSON document instead of human output.</param>
/// <param name="DiffBase">The <c>--diff-base</c> git ref for the Freeze tripwire, or null to skip it.</param>
/// <param name="WorkingDirectory">The directory solution discovery walks up from.</param>
/// <param name="NoCache">Whether to bypass the persisted extraction cache entirely (no read, no write).</param>
/// <param name="Binlog">
///     The explicit <c>--binlog</c> path to replay instead of a design-time build, or null to auto-select
///     (a valid build capture if one exists, else a design-time build). Roslyn-free — the gate acts on it.
/// </param>
internal sealed record CheckRequest(
    string? Solution,
    string? Spec,
    bool Json,
    string? DiffBase,
    string WorkingDirectory,
    bool NoCache,
    string? Binlog);