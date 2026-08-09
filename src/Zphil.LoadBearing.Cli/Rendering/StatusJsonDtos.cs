using Zphil.LoadBearing.Checking;

namespace Zphil.LoadBearing.Cli.Rendering;

// The wire shape of `status --json` — its own document with its own schemaVersion (2), distinct from
// `check --json`. Serialized camelCase, indented, nulls omitted. The two workspace slots below are additive
// and null (omitted) on every run whose workspace loaded, so the schema stays version 2 and a clean document
// is byte-identical.

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
internal sealed record StatusJson(
    int SchemaVersion,
    string Solution,
    string SpecAssembly,
    IReadOnlyList<StatusRuleJson> Rules,
    IReadOnlyList<string>? WorkspaceDiagnostics,
    bool? ModelIncomplete,
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
