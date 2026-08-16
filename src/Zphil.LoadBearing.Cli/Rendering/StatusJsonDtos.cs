using Zphil.LoadBearing.Checking;

namespace Zphil.LoadBearing.Cli.Rendering;

// The wire shape of `status --json` — its own document with its own schemaVersion (2), distinct from
// `check --json`. Serialized camelCase, indented, nulls omitted. The four workspace slots below are
// additive and null (omitted) on every run whose workspace loaded and that no solution filter narrowed, so
// the schema stays version 2 and a clean document is byte-identical.

/// <summary>The root <c>status --json</c> document.</summary>
/// <param name="WorkspaceDiagnostics">
///     The workspace-load diagnostics, or null (omitted) when there were none — the same array
///     <c>check --json</c> carries, so <c>arch_status</c> stops being a surface where they are destroyed.
/// </param>
/// <param name="ModelIncomplete">
///     <see langword="true" /> when a project failed to load, so the burndown below counts only what did
///     load; null (omitted) otherwise. Stamped whether or not <c>--allow-workspace-diagnostics</c> opted out
///     of failing closed — it states the fact about the model, not the exit code.
/// </param>
/// <param name="FailedProjects">
///     Which projects failed to load — solution-relative, forward-slashed <c>.csproj</c> paths — or null
///     (omitted) when none did: the evidence behind <see cref="ModelIncomplete" />, which
///     <c>workspaceDiagnostics</c> cannot be read for, since MSBuild's words about a fatal failure and about
///     an ordinary restore warning arrive in the same shape.
/// </param>
/// <param name="UncheckedProjects">
///     Which projects the solution declares that this run never checked — solution-relative,
///     forward-slashed <c>.csproj</c> paths — or null (omitted) when the burndown covers the whole solution.
///     Non-empty only under a <c>.slnf</c> solution filter. An unchecked project declares no types and so
///     contributes no violations, which reads as burndown rather than as absence: every count below is low
///     by whatever these projects hold. Measured as what the solution declares minus what loaded, so a
///     filter that narrows nothing omits the key.
/// </param>
internal sealed record StatusJson(
    int SchemaVersion,
    string Solution,
    string SpecAssembly,
    IReadOnlyList<StatusRuleJson> Rules,
    IReadOnlyList<string>? WorkspaceDiagnostics,
    bool? ModelIncomplete,
    IReadOnlyList<string>? FailedProjects,
    IReadOnlyList<string>? UncheckedProjects,
    StatusSummaryJson Summary);

/// <summary>
///     One rule's status. <see cref="Ratchet" /> is populated for ratcheted rules (Migrate and Quarantine
///     containment). <see cref="Posture" /> and <see cref="Status" /> are the model's own enums, cased for
///     the wire by <see cref="LoadBearingJson.Options" /> — the same values <c>check --json</c> writes.
/// </summary>
internal sealed record StatusRuleJson(
    string Id,
    Posture Posture,
    RuleStatus Status,
    int Violations,
    int Warnings,
    RatchetStatusJson? Ratchet);

/// <summary>
///     A ratcheted rule's state: the baseline path, capture flag, and burndown counts. <see cref="Promotable" />
///     is populated for Migrate only (omitted for Quarantine containment — its promotion is a human decision).
/// </summary>
internal sealed record RatchetStatusJson(
    string BaselinePath,
    bool Captured,
    int Remaining,
    int NewViolations,
    int Stale,
    bool? Promotable);

/// <summary>The roll-up: rule counts plus the ratchet burndown totals.</summary>
internal sealed record StatusSummaryJson(
    int RulesChecked,
    int RulesPassed,
    int RulesFailed,
    int RulesSkipped,
    int GrandfatheredRemaining,
    int FixedAwaitingAcceptance);
