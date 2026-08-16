using Zphil.LoadBearing.Cli.Pipeline;
using Zphil.LoadBearing.Cli.Rendering;

namespace Zphil.LoadBearing.Cli.Verbs;

/// <summary>The parsed inputs to a <c>check</c> run — free of Roslyn types so it crosses the MSBuild gate.</summary>
/// <param name="Solution">The positional solution argument (a file, a directory, or null for cwd walk-up).</param>
/// <param name="Spec">The <c>--spec</c> value (a built DLL or a solution-member csproj), or null for convention.</param>
/// <param name="Json">Whether to emit the machine-readable JSON document instead of human output.</param>
/// <param name="DiffBase">The <c>--diff-base</c> git ref for the Quarantine tripwire, or null to skip it.</param>
/// <param name="WorkingDirectory">The directory solution discovery walks up from.</param>
/// <param name="NoCache">Whether to bypass the persisted extraction cache entirely (no read, no write).</param>
/// <param name="Binlog">
///     The explicit <c>--binlog</c> path to replay instead of a design-time build, or null to auto-select
///     (a valid build capture if one exists, else a design-time build). Roslyn-free — the gate acts on it.
/// </param>
/// <param name="AllowWorkspaceDiagnostics">
///     Whether to check against the partial model when a project fails to load. Default (<c>false</c>):
///     any workspace-load failure diagnostic fails the run with exit 2, overriding the 0/1 verdict, because
///     a rule that "passes" only because a project did not load is worse than no answer. <c>true</c> prints
///     those diagnostics as warnings instead and lets the run exit 0/1. Keys strictly
///     on workspace-load failures, never on the advisory merge notes or NuGetAudit advisories (NU19xx)
///     that share the diagnostics stream.
/// </param>
/// <param name="Sarif">
///     The <c>--sarif</c> path to write a SARIF 2.1.0 report to — a third renderer over the same result
///     model (for GitHub/ADO code scanning et al.) — or null to write none. Human and <c>--json</c> output
///     are byte-for-byte unchanged either way; the only added observable is a <c>wrote &lt;path&gt;</c>
///     line in human mode. Roslyn-free, so the record still crosses the MSBuild gate.
/// </param>
/// <param name="Rules">
///     The <c>--rules</c> allow-list — rule-ID globs, semicolon-separated — or null for every rule in the
///     spec. It narrows what <em>runs</em>, not what is displayed, so the summary and the 0/1 verdict cover
///     the subset alone. A filter that matches no rule refuses the run rather than checking nothing.
/// </param>
/// <param name="Grain">
///     The floor on how much of each rule the JSON document renders: never finer than this, and a caller
///     whose transport has a response budget may take it coarser still (<see cref="IResponseFitter" />).
///     The opposite knob to <see cref="Rules" /> and independent of it — every selected rule is reported at
///     every rung, in less detail. Human output ignores it: a terminal has no budget to overrun.
/// </param>
internal sealed record CheckRequest(
    string? Solution,
    string? Spec,
    bool Json,
    string? DiffBase,
    string WorkingDirectory,
    bool NoCache,
    string? Binlog,
    bool AllowWorkspaceDiagnostics,
    string? Sarif,
    string? Rules,
    DocumentGrain Grain) : IReplayableRequest;
