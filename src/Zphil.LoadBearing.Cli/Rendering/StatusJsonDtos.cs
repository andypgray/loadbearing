using Zphil.LoadBearing.Checking;

namespace Zphil.LoadBearing.Cli.Rendering;

// The wire shape of `status --json` — its own document with its own schemaVersion (2), distinct from
// `check --json`. Serialized camelCase, indented, nulls omitted. The six workspace slots below are
// additive and null (omitted) on every run whose workspace loaded, whose NuGet packages resolved, whose
// solution is all C# and that no solution filter narrowed, so the schema stays version 2 and a clean
// document is byte-identical. The ratchet's two measure counts are additive the same way, on both the
// per-rule block and the summary. The two site totals beside them are not: a burndown unit that appears
// only sometimes is worse than one more integer, so they are written whatever they read, and the schema
// stays at 2 because a consumer that never asked for them is unaffected by two more keys.

/// <summary>The root <c>status --json</c> document.</summary>
/// <param name="SchemaVersion">The burndown document's schema version — 2.</param>
/// <param name="Solution">
///     The solution's file name — the run's subject, and never a path, so the document is
///     machine-independent.
/// </param>
/// <param name="SpecAssembly">The spec DLL's file name — which spec's rules answered.</param>
/// <param name="Rules">One entry per rule in the spec — status carries no narrowing knob.</param>
/// <param name="WorkspaceDiagnostics">
///     The workspace-load diagnostics, or null (omitted) when there were none — the same array
///     <c>check --json</c> carries, so <c>arch_status</c> stops being a surface where they are destroyed.
/// </param>
/// <param name="ModelIncomplete">
///     <see langword="true" /> when a project failed to load or a project's NuGet packages are not in the
///     model, so the burndown below counts only what the model holds; null (omitted) otherwise. Stamped
///     whether or not <c>--allow-workspace-diagnostics</c> opted out of failing closed — it states the fact
///     about the model, not the exit code.
/// </param>
/// <param name="FailedProjects">
///     Which projects failed to load — solution-relative, forward-slashed <c>.csproj</c> paths — or null
///     (omitted) when none did: half the evidence behind <see cref="ModelIncomplete" />, which
///     <c>workspaceDiagnostics</c> cannot be read for, since MSBuild's words about a fatal failure and about
///     an ordinary restore warning arrive in the same shape.
/// </param>
/// <param name="RestoreFailedProjects">
///     Which projects' NuGet packages are not in the model — solution-relative, forward-slashed
///     <c>.csproj</c> paths — or null (omitted) when none are. The burndown reads low in a quieter way here
///     than it does for <see cref="FailedProjects" />: these projects declare all their types, so the rules
///     over them run and report — a rule whose target is a package the restore never fetched simply finds
///     nothing to count.
/// </param>
/// <param name="UncheckedProjects">
///     Which projects the solution declares that this run never checked — solution-relative,
///     forward-slashed <c>.csproj</c> paths — or null (omitted) when the burndown covers the whole solution.
///     Non-empty only under a <c>.slnf</c> solution filter. An unchecked project declares no types and so
///     contributes no violations, which reads as burndown rather than as absence: every count below is low
///     by whatever these projects hold. Measured as what the solution declares minus what loaded, so a
///     filter that narrows nothing omits the key.
/// </param>
/// <param name="UnsupportedProjects">
///     Which projects the solution declares that this product cannot read — each a solution-relative,
///     forward-slashed project path with the reason — or null (omitted) for an all-C# solution. The burndown
///     reads low here the same way <see cref="UncheckedProjects" /> makes it read low, and permanently: a
///     project the extractor cannot read will never contribute a violation to burn down, so its zero is a
///     property of this product rather than progress the team made.
/// </param>
/// <param name="Summary">The roll-up: rule counts plus the ratchet burndown totals.</param>
internal sealed record StatusJson(
    int SchemaVersion,
    string Solution,
    string SpecAssembly,
    IReadOnlyList<StatusRuleJson> Rules,
    IReadOnlyList<string>? WorkspaceDiagnostics,
    bool? ModelIncomplete,
    IReadOnlyList<string>? FailedProjects,
    IReadOnlyList<string>? UncheckedProjects,
    IReadOnlyList<string>? RestoreFailedProjects,
    IReadOnlyList<UnsupportedProjectStamp>? UnsupportedProjects,
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
/// <param name="BaselinePath">The rule's baseline file, solution-relative with forward slashes.</param>
/// <param name="Captured">Whether a baseline section exists for the rule at all.</param>
/// <param name="Remaining">How many violations the baseline currently blesses — the burndown, in pairs.</param>
/// <param name="RemainingSites">
///     How many <em>sites</em> those pairs cover — the burndown at the grain the ratchet measures, and the
///     unit a team actually works off. Unconditional rather than omitted where it equals
///     <see cref="Remaining" />: a burndown figure that vanishes when it happens to agree with its
///     neighbour is one a consumer has to reconstruct, and it is computable whether or not any entry has
///     recorded a count yet.
/// </param>
/// <param name="NewViolations">How many violations are red — new code in the old pattern, growth included.</param>
/// <param name="Stale">How many captured entries no current violation matched.</param>
/// <param name="Shrunk">
///     How many matched edge entries came in under the count they record, or null (omitted) when none did.
/// </param>
/// <param name="Uncounted">
///     How many matched edge entries record no count at all, or null (omitted) when none. Both measures are
///     omitted at zero, so a rule whose section is fully counted and still holding carries neither key.
/// </param>
/// <param name="Promotable">Whether the Migrate ratchet has burned to zero; omitted for Quarantine.</param>
internal sealed record RatchetStatusJson(
    string BaselinePath,
    bool Captured,
    int Remaining,
    int RemainingSites,
    int NewViolations,
    int Stale,
    int? Shrunk,
    int? Uncounted,
    bool? Promotable);

/// <summary>The roll-up: rule counts plus the ratchet burndown totals.</summary>
/// <param name="RulesChecked">How many rules the run evaluated.</param>
/// <param name="RulesPassed">How many of them passed.</param>
/// <param name="RulesFailed">How many of them failed.</param>
/// <param name="RulesSkipped">How many reached no verdict.</param>
/// <param name="GrandfatheredRemaining">The whole solution's burndown, in pairs.</param>
/// <param name="GrandfatheredSites">
///     The same burndown in sites — <see cref="RatchetStatusJson.RemainingSites" /> totalled, and
///     unconditional for its reason.
/// </param>
/// <param name="FixedAwaitingAcceptance">Stale entries across every rule.</param>
/// <param name="Shrunk">Shrunk entries across every rule, or null (omitted) when there are none.</param>
/// <param name="Uncounted">Uncounted entries across every rule, or null (omitted) when there are none.</param>
internal sealed record StatusSummaryJson(
    int RulesChecked,
    int RulesPassed,
    int RulesFailed,
    int RulesSkipped,
    int GrandfatheredRemaining,
    int GrandfatheredSites,
    int FixedAwaitingAcceptance,
    int? Shrunk,
    int? Uncounted);
