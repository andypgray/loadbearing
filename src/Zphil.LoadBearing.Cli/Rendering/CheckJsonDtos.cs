namespace Zphil.LoadBearing.Cli.Rendering;

// The wire shape of `check --json` (schemaVersion 3 — Quarantine containment evaluates and ratchets, and a
// Quarantine tripwire warns), pinned by a golden test. Serialized camelCase, indented, nulls omitted.
// Clustered in one file: these records are one cohesive DTO, not product types.
// The additive `targetMember` slot (a banned member's raw symbol ID for a memberUse violation, GRAMMAR
// §4.5) and `subjectMember` slot (an offending member's raw symbol ID for a memberShape violation, GRAMMAR
// §4.6) are null on every other kind and so omitted — the schema stays version 3, byte-identical for specs
// without a member-target or member-subject rule. The `modelIncomplete` slot is additive the same way: null
// (omitted) on every run whose workspace loaded, so a clean document is unchanged.

/// <summary>The root JSON document — the only thing written to stdout in <c>--json</c> mode.</summary>
/// <param name="ModelIncomplete">
///     <see langword="true" /> when a project failed to load, so every verdict below was reached against a
///     partial model; null (and so omitted) otherwise. The fact, not the exit code: it is stamped whether or
///     not <c>--allow-workspace-diagnostics</c> opted out of failing closed, which is what lets
///     <c>arch_check</c> tell a client the answer is untrustworthy on a surface that has no exit code.
/// </param>
internal sealed record CheckJson(
    int SchemaVersion,
    string Solution,
    string SpecAssembly,
    string? DiffBase,
    IReadOnlyList<RuleJson> Rules,
    IReadOnlyList<string> WorkspaceDiagnostics,
    bool? ModelIncomplete,
    SummaryJson Summary);

/// <summary>
///     One rule's result. <see cref="Baseline" /> is populated for ratcheted rules (Migrate and Quarantine
///     containment).
/// </summary>
internal sealed record RuleJson(
    string Id,
    string Posture,
    string Status,
    string Sentence,
    string Because,
    string? Fix,
    string? SkipReason,
    BaselineJson? Baseline,
    IReadOnlyList<ViolationJson> Violations,
    IReadOnlyList<WarningJson> Warnings);

/// <summary>A ratcheted rule's state: its baseline path and the grandfathered/stale counts.</summary>
internal sealed record BaselineJson(string Path, int Grandfathered, int Stale);

/// <summary>One violation; the null slots are omitted per kind.</summary>
internal sealed record ViolationJson(
    string Kind,
    string? Source,
    string? Target,
    string? TargetMember,
    string? Subject,
    string? SubjectMember,
    string? Detail,
    IReadOnlyList<SiteJson> Sites);

/// <summary>A single reference or declaration site (relative, forward-slash path).</summary>
internal sealed record SiteJson(string File, int Line);

/// <summary>A non-fatal warning.</summary>
internal sealed record WarningJson(string Kind, string Message);

/// <summary>The roll-up counts.</summary>
internal sealed record SummaryJson(
    int RulesChecked,
    int RulesPassed,
    int RulesFailed,
    int RulesSkipped,
    int Violations,
    int Warnings);