namespace Zphil.LoadBearing.Cli.Rendering;

// The wire shape of `status --json` — its own document with its own schemaVersion (2), distinct from
// `check --json`. Serialized camelCase, indented, nulls omitted. A burndown view, not a gate.

/// <summary>The root <c>status --json</c> document.</summary>
internal sealed record StatusJson(
    int SchemaVersion,
    string Solution,
    string SpecAssembly,
    IReadOnlyList<StatusRuleJson> Rules,
    StatusSummaryJson Summary);

/// <summary>
///     One rule's status. <see cref="Ratchet" /> is populated for ratcheted rules (Migrate and Freeze
///     containment).
/// </summary>
internal sealed record StatusRuleJson(
    string Id,
    string Posture,
    string Status,
    int Violations,
    int Warnings,
    RatchetStatusJson? Ratchet);

/// <summary>
///     A ratcheted rule's state: the baseline path, capture flag, and burndown counts. <see cref="Promotable" />
///     is populated for Migrate only (omitted for Freeze containment — its promotion is a human decision).
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