using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Roslyn.Diagnostics;

namespace Zphil.LoadBearing.Cli.Rendering;

// The wire shape of `check --json` (schemaVersion 3 — Quarantine containment evaluates and ratchets, and a
// Quarantine tripwire warns), pinned by a golden test. Serialized camelCase, indented, nulls omitted.
// Clustered in one file: these records are one cohesive DTO, not product types.
// The additive `targetMember` slot (a banned member's raw symbol ID for a memberUse violation, GRAMMAR
// §4.5) and `subjectMember` slot (an offending member's raw symbol ID for a memberShape violation, GRAMMAR
// §4.6) are null on every other kind and so omitted — the schema stays version 3, byte-identical for specs
// without a member-target or member-subject rule. The `modelIncomplete`, `failedProjects`,
// `restoreFailedProjects`, `uncheckedProjects`, `unsupportedProjects`, `multiTargetedProjects` and
// `rulesFilter` slots are additive the same way: null (omitted) on every run whose workspace loaded, whose
// NuGet packages resolved, that no solution filter narrowed, whose solution is all C# and single-framework,
// and that checked the whole spec — so a clean document is unchanged.
// The `grain` slot is additive in the same sense and absent from every full-grain report, which is every
// report the CLI writes unless asked otherwise; it holds the schema at version 3, as the survey's own ladder
// held it at 1 — a consumer reading a full document cannot tell it exists. The per-rule `violationCount` and
// per-violation `siteCount` are unconditional at every grain: while each stood in for its elided array, a
// defensive absent-means-zero read answered 0 wherever the array was rendered instead — silently wrong
// precisely for a failed rule — so each is always written, ahead of the array it summarizes.
// CheckJson's slot order is the verdict first, and deliberately: the request echo, then the trust stamps,
// then `summary`, then `rules`, with `workspaceDiagnostics` last. System.Text.Json writes a record's
// declaration order verbatim, so this list is the wire order. Below the ladder's coarsest rung a reader with
// a response budget cuts at the last newline that fits, and that cut lands inside `rules` — the bulk — so a
// trailing roll-up was amputated exactly when it mattered most, check overrunning only when it is red-heavy.
// The stamps precede `summary` so a caveat never arrives after the counts it invalidates.
// `workspaceDiagnostics` stays trailing because it is MSBuild's evidence rather than the verdict, it has no
// ceiling, and the actionable half of it is already hoisted into `failedProjects` and
// `restoreFailedProjects`. On a clean run every stamp is omitted, so ordering them costs the unchanged
// document nothing: key order, not shape, and schemaVersion stays 3.

/// <summary>The root JSON document — the only thing written to stdout in <c>--json</c> mode.</summary>
/// <param name="Grain">
///     <c>overview</c> when each violation's sites were elided, <c>skeleton</c> when the violations went with
///     them, or null (omitted) at full grain — so a document that says nothing about grain is the complete
///     one, and a consumer can tell a coarser report from a cleaner solution without diffing it. A coarser
///     report is never a narrower one: every rule the run selected is here at every rung, with its verdict
///     and its prose, which is what makes an automatic degrade safe on a surface the caller cannot re-ask.
/// </param>
/// <param name="RulesFilter">
///     The rule-ID globs the run was narrowed to, or null (omitted) when it checked the whole spec. Present,
///     it says that <c>rules</c> and <c>summary</c> below cover a subset — the counts are of what ran, so a
///     clean narrowed document is not a clean solution.
/// </param>
/// <param name="ModelIncomplete">
///     <see langword="true" /> when a project failed to load or a project's NuGet packages are not in the
///     model, so every verdict below was reached against a partial model; null (and so omitted) otherwise.
///     The fact, not the exit code: it is stamped whether or not <c>--allow-workspace-diagnostics</c> opted
///     out of failing closed, which is what lets <c>arch_check</c> tell a client the answer is untrustworthy
///     on a surface that has no exit code.
/// </param>
/// <param name="FailedProjects">
///     Which projects failed to load — solution-relative, forward-slashed <c>.csproj</c> paths — or null
///     (omitted) when none did. Half the evidence behind <see cref="ModelIncomplete" />, and the reason it
///     needs its own slot: <c>workspaceDiagnostics</c> carries MSBuild's words about the load, which name a
///     failure and an ordinary restore warning in exactly the same shape, so a client cannot recover this
///     from them. Absent on a clean run, so a clean document is unchanged.
/// </param>
/// <param name="RestoreFailedProjects">
///     Which projects' NuGet packages are not in the model — solution-relative, forward-slashed
///     <c>.csproj</c> paths — or null (omitted) when none are. The other evidence behind
///     <see cref="ModelIncomplete" />, and beside <see cref="FailedProjects" /> rather than folded in because
///     these projects <em>loaded</em>: every type they declare is in the model and only their package edges
///     are missing, so a rule about a package reads as inert rather than as violated. The remedy differs too
///     — <c>dotnet restore</c>, rather than <c>dotnet build</c>. One slot covers both ways the packages can be
///     absent, a restore that ran and failed and one that never ran, because a consumer told which could do
///     nothing different with the answer.
/// </param>
/// <param name="UncheckedProjects">
///     Which projects the solution declares that this run never checked — solution-relative,
///     forward-slashed <c>.csproj</c> paths — or null (omitted) when the run covered the whole solution.
///     Non-empty only under a <c>.slnf</c> solution filter, and beside <see cref="FailedProjects" /> rather
///     than folded into it because it scopes the verdict instead of invalidating it: the rules below all ran
///     and all answered, over less. A clean report carrying this slot covers a subset of the solution, which
///     is the reading no exit code can give a client. Measured as what the solution declares minus what
///     loaded, never read from the filter's own selection — a filter whose transitive project references
///     pull the rest of the solution in narrows nothing and omits the key.
/// </param>
/// <param name="UnsupportedProjects">
///     Which projects the solution declares that this product cannot read — each a solution-relative,
///     forward-slashed project path with the reason — or null (omitted) for an all-C# solution. Beside
///     <see cref="UncheckedProjects" /> rather than <see cref="FailedProjects" /> and for its reason: it
///     scopes the verdict instead of invalidating it, so it never reaches <see cref="ModelIncomplete" /> and
///     every rule below still ran and answered, over a universe that never included these projects. A clean
///     verdict over a polyglot solution is the exact reading this slot qualifies — no rule can be violated
///     in a project the model does not contain, and the report had no way to say so.
/// </param>
/// <param name="MultiTargetedProjects">
///     Which projects arrived as several compilations, because one <c>.csproj</c> targets several frameworks
///     — each with every framework it was read from and, where its frameworks share a type, the one those
///     types' facts came from — or null (omitted) when every project targets a single framework. Beside
///     <see cref="UnsupportedProjects" /> and
///     for its reason: it scopes the verdict rather than invalidating it, so it never reaches
///     <see cref="ModelIncomplete" /> and every rule below ran and answered — against one framework's view of
///     these projects. That is what the slot exists to say: a rule about a type both frameworks declare was
///     checked against <c>factsFollow</c> alone, and whatever another framework's <c>#if</c> guards was never
///     in the model to violate it. Never elided by grain, on the same reasoning as its neighbour: it is
///     bounded by the solution rather than by the codebase, and a coarser report is where a reader most needs
///     it. <c>workspaceDiagnostics</c> carries the same fact as an English sentence, and only for the
///     projects whose frameworks actually collapsed a type; this is the keyed form, and it covers the rest.
/// </param>
internal sealed record CheckJson(
    int SchemaVersion,
    string Solution,
    string SpecAssembly,
    string? Grain,
    string? DiffBase,
    IReadOnlyList<string>? RulesFilter,
    bool? ModelIncomplete,
    IReadOnlyList<string>? FailedProjects,
    IReadOnlyList<string>? UncheckedProjects,
    IReadOnlyList<string>? RestoreFailedProjects,
    IReadOnlyList<UnsupportedProjectStamp>? UnsupportedProjects,
    IReadOnlyList<MultiTargetedProject>? MultiTargetedProjects,
    SummaryJson Summary,
    IReadOnlyList<RuleJson> Rules,
    IReadOnlyList<string> WorkspaceDiagnostics);

/// <summary>
///     One rule's result. <see cref="Baseline" /> is populated for ratcheted rules (Migrate and Quarantine
///     containment). <see cref="Posture" /> and <see cref="Status" /> are the model's own enums — the
///     camelCase wire spelling is <see cref="LoadBearingJson.Options" />'s to apply, so a renderer cannot
///     write a value no member names.
/// </summary>
/// <param name="ViolationCount">
///     How many violations the rule found, at every grain. The count is the stable key a consumer scripts
///     against, so it never substitutes for <see cref="Violations" /> or yields to it — while it stood in
///     for the elided array alone, a defensive absent-means-zero read answered 0 exactly when the rule had
///     failed. Declared ahead of the array it summarizes, so a downstream truncator that cuts inside the
///     bulk has already written the number. Zero means "none found": beside <c>status: passed</c> it is
///     what makes a skeleton report a verdict.
/// </param>
/// <param name="Violations">
///     This rule's violations — <see cref="ViolationCount" />'s expansion — or null (omitted) at skeleton
///     grain, the one thing that grain elides beyond overview's. Null here is "not rendered at this grain",
///     never "none found": a passing rule renders an empty array, and the count tells the two apart.
/// </param>
/// <param name="SubjectTypes">
///     How many types the rule's subject materialized to, present only alongside
///     <see cref="SubjectGeneratedTypes" />. It is the denominator that makes that number readable — "803
///     of 804" and "803 of 90,000" are different findings — and it appears nowhere else in the document.
/// </param>
/// <param name="SubjectGeneratedTypes">
///     How many of <see cref="SubjectTypes" /> a generator emitted, or null (omitted) when none were. The
///     pair is populated together and only when the count is non-zero, so it is self-extinguishing:
///     narrowing the rule with <c>.Authored()</c> empties it and both keys go away. Rendered at every
///     grain — it is two integers, and the coarser the report the more a reader needs to know the verdict
///     is partly about code nobody wrote.
/// </param>
internal sealed record RuleJson(
    string Id,
    Posture Posture,
    RuleStatus Status,
    string Sentence,
    string Because,
    string? Fix,
    string? SkipReason,
    BaselineJson? Baseline,
    int? SubjectTypes,
    int? SubjectGeneratedTypes,
    int ViolationCount,
    IReadOnlyList<ViolationJson>? Violations,
    IReadOnlyList<WarningJson> Warnings);

/// <summary>A ratcheted rule's state: its baseline path and the grandfathered/stale counts.</summary>
internal sealed record BaselineJson(string Path, int Grandfathered, int Stale);

/// <summary>One violation; the null slots are omitted per kind.</summary>
/// <param name="SiteCount">
///     How many sites the violation occurs at, at every grain — <see cref="Sites" /> is its expansion, not
///     its replacement, on the same reasoning as <see cref="RuleJson" />'s violation count. A coarser
///     report still says how much work each violation is, which is the one thing a reader would otherwise
///     have to re-run the check at full grain to learn.
/// </param>
/// <param name="Sites">
///     Where the violation occurs — <see cref="SiteCount" />'s expansion — or null (omitted) from overview
///     grain down: the first thing the ladder elides, because sites scale with the codebase while
///     everything above them scales with the spec. Null is "not rendered at this grain", never "none
///     found"; the count carries the number.
/// </param>
internal sealed record ViolationJson(
    ViolationKind Kind,
    string? Source,
    string? Target,
    string? TargetMember,
    string? Subject,
    string? SubjectMember,
    string? Detail,
    int SiteCount,
    IReadOnlyList<SiteJson>? Sites);

/// <summary>A single reference or declaration site (relative, forward-slash path).</summary>
internal sealed record SiteJson(string File, int Line);

/// <summary>A non-fatal warning.</summary>
internal sealed record WarningJson(CheckWarningKind Kind, string Message);

/// <summary>The roll-up counts.</summary>
internal sealed record SummaryJson(
    int RulesChecked,
    int RulesPassed,
    int RulesFailed,
    int RulesSkipped,
    int Violations,
    int Warnings);
