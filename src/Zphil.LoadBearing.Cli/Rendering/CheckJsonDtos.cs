using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Roslyn.Diagnostics;

namespace Zphil.LoadBearing.Cli.Rendering;

// The wire shape of `check --json` (schemaVersion 3), pinned by a golden test. Serialized camelCase, indented,
// nulls omitted. Clustered in one file: these records are one cohesive DTO, not product types.
// The version moves only for a change of shape. A slot that is null on every run with nothing to say — a
// kind-specific member or project slot, a ratchet measure at zero, a trust stamp for a workspace that loaded
// whole and unfiltered, the `grain` of a full-grain report — is omitted, so a document that needs none of
// them is byte-identical to one written before the slot existed; and a widened enum (`caution`,
// `cautionedScopeTouched`) is a value a consumer meets, not a new shape. The per-rule `violationCount` and
// per-violation `siteCount` are unconditional at every grain, each declared ahead of the array it
// summarizes. Declaration order IS the wire order (System.Text.Json writes it verbatim), and CheckJson's is
// a deliberate ranking: the request echo, then the trust stamps, then `summary`, then `rules`, with
// `workspaceDiagnostics` last — a stamp never arrives after the counts it invalidates, and a downstream
// cut lands in the bulk rather than on the roll-up.

/// <summary>The root JSON document — the only thing written to stdout in <c>--json</c> mode.</summary>
/// <param name="SchemaVersion">The report's schema version — 3.</param>
/// <param name="Solution">
///     The solution's file name — the run's subject, and never a path, so the document is
///     machine-independent.
/// </param>
/// <param name="SpecAssembly">The spec DLL's file name — which spec's rules answered.</param>
/// <param name="Grain">
///     <c>overview</c> when each violation's sites were elided, <c>skeleton</c> when the violations went with
///     them, <c>index</c> when the report is down to a verdict per rule id, or null (omitted) at full grain —
///     so a document that says nothing about grain is the complete one, and a consumer can tell a coarser
///     report from a cleaner solution without diffing it. A coarser report is never a narrower one: every
///     rule the run selected is here at every rung, with its verdict, which is what makes an automatic
///     degrade safe on a surface the caller cannot re-ask.
/// </param>
/// <param name="DiffBase">
///     The git ref the Quarantine tripwire compared against, or null (omitted) when the run took no diff.
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
/// <param name="Summary">
///     The roll-up counts — of what ran, so under <see cref="RulesFilter" /> they cover the subset.
/// </param>
/// <param name="Rules">One entry per rule the run selected, present at every grain.</param>
/// <param name="WorkspaceDiagnostics">
///     MSBuild's own words about the load — evidence rather than verdict; empty on a clean load, and null
///     (omitted) at index grain where <see cref="WorkspaceDiagnosticCount" /> stands in. One entry per
///     project per framework per complaint, so it is the one array on this document with no ceiling that is
///     not a function of the spec: measured on a 9-project bed it was 86,518 characters beside a
///     2,709-character rule list. Eliding it at the floor rung costs a reader nothing they cannot get back —
///     its actionable half is already keyed in <see cref="FailedProjects" /> and
///     <see cref="RestoreFailedProjects" />, and <see cref="ModelIncomplete" /> still says the verdict was
///     reached against a partial model.
/// </param>
/// <param name="WorkspaceDiagnosticCount">
///     How many diagnostics the elision dropped, present only when <see cref="WorkspaceDiagnostics" /> is
///     elided at index grain and there were some — never a bare <c>0</c>, so a clean load's report loses a
///     key that said nothing rather than gaining one.
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
    IReadOnlyList<string>? WorkspaceDiagnostics,
    int? WorkspaceDiagnosticCount);

/// <summary>
///     One rule's result. <see cref="Baseline" /> is populated for ratcheted rules (Migrate and Quarantine
///     containment). <see cref="Posture" /> and <see cref="Status" /> are the model's own enums — the
///     camelCase wire spelling is <see cref="LoadBearingJson.Options" />'s to apply, so a renderer cannot
///     write a value no member names.
/// </summary>
/// <param name="Id">
///     The post-desugar rule ID, at every grain. It is what makes the floor rung a narrowing menu rather
///     than only the smallest answer: these are the strings <c>rules</c> globs match, and
///     <c>arch_explain</c> takes one of them and returns that rule whole.
/// </param>
/// <param name="Posture">The rule's declared posture.</param>
/// <param name="Status">The evaluation status.</param>
/// <param name="Sentence">
///     The rule's rendered English sentence, or null (omitted) at index grain — the one thing that grain
///     elides beyond skeleton's, along with its two neighbours below.
/// </param>
/// <param name="Because">The rule's rationale prose, or null (omitted) at index grain.</param>
/// <param name="Fix">
///     The rule's fix hint, or null (omitted) when the spec declares none — and at index grain, where the
///     other two go with it.
/// </param>
/// <param name="SkipReason">
///     Why the run reached no verdict for this rule, or null (omitted) when it was evaluated.
/// </param>
/// <param name="Baseline">The ratchet state, or null (omitted) for a non-ratcheted rule.</param>
/// <param name="ViolationCount">
///     How many violations the rule found, at every grain. The count is the stable key a consumer scripts
///     against, so it never substitutes for <see cref="Violations" /> or yields to it — a count present only
///     when the array is elided lets a defensive absent-means-zero read answer 0 exactly when the rule has
///     failed. Declared ahead of the array it summarizes, so a downstream truncator that cuts inside the
///     bulk has already written the number. Zero means "none found": beside <c>status: passed</c> it is
///     what makes a skeleton report a verdict.
/// </param>
/// <param name="Violations">
///     This rule's violations — <see cref="ViolationCount" />'s expansion — or null (omitted) from skeleton
///     grain down, the one thing that grain elides beyond overview's. Null here is "not rendered at this
///     grain", never "none found": a passing rule renders an empty array, and the count tells the two apart.
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
/// <param name="Warnings">The rule's non-fatal warnings; empty when there are none.</param>
/// <param name="Citation">
///     The canonical page the rule's reason rests on, or null (omitted) when the rule cites none and at
///     index grain, where it is elided with the rest of the prose. Written after <see cref="Fix" /> rather
///     than beside <see cref="Because" /> so a reader keeps the key order it already scripts against.
/// </param>
internal sealed record RuleJson(
    string Id,
    Posture Posture,
    RuleStatus Status,
    string? Sentence,
    string? Because,
    string? Fix,
    string? Citation,
    string? SkipReason,
    BaselineJson? Baseline,
    int? SubjectTypes,
    int? SubjectGeneratedTypes,
    int ViolationCount,
    IReadOnlyList<ViolationJson>? Violations,
    IReadOnlyList<WarningJson> Warnings);

/// <summary>A ratcheted rule's state: its baseline path and the grandfathered/stale counts.</summary>
/// <param name="Path">The rule's baseline file, solution-relative with forward slashes.</param>
/// <param name="Grandfathered">
///     How many of the rule's violations the baseline blessed. Counts what passed, so a pair that carried
///     more sites than its entry records is not here — it is red, in <c>violations</c>, carrying
///     <see cref="ViolationJson.GrandfatheredSiteCount" />.
/// </param>
/// <param name="Stale">
///     How many captured entries no current violation matched — fixed debt awaiting
///     <c>baseline --accept-reductions</c>.
/// </param>
/// <param name="Shrunk">
///     How many matched edge entries came in under the site count they record — real reductions, which pass
///     and await <c>baseline --accept-reductions</c> to lower the recorded count — or null (omitted) when
///     there are none.
/// </param>
/// <param name="Uncounted">
///     How many matched edge entries record no site count at all, so they grandfather their pair at any
///     size, or null (omitted) when there are none. Self-extinguishing like its neighbour: a fully counted
///     section carries neither key, so a document from a baseline written after the measure existed is
///     unchanged by both.
/// </param>
internal sealed record BaselineJson(string Path, int Grandfathered, int Stale, int? Shrunk, int? Uncounted);

/// <summary>One violation; the null slots are omitted per kind.</summary>
/// <param name="Kind">Which shape of violation this is — it decides which slots below are populated.</param>
/// <param name="Source">The referencing type's full name, for dependency kinds; null (omitted) otherwise.</param>
/// <param name="Target">The referenced type's full name, for dependency kinds; null (omitted) otherwise.</param>
/// <param name="TargetMember">
///     The banned member's raw symbol ID, for a <c>memberUse</c> violation; null (omitted) otherwise.
/// </param>
/// <param name="Subject">The offending type's full name, for shape kinds; null (omitted) otherwise.</param>
/// <param name="SubjectMember">
///     The offending member's raw symbol ID, for a <c>memberShape</c> violation; null (omitted) otherwise.
/// </param>
/// <param name="SubjectProject">
///     The offending project's name, for a <c>projectShape</c> violation; null (omitted) otherwise. The
///     name, not the <c>project:</c> identity form — the identity is a baseline key and this document
///     spells subjects the way a reader spells them.
/// </param>
/// <param name="Package">
///     The offending package's name, for the per-package <c>projectShape</c> violations a
///     <c>MustReferenceNoPackages</c> rule mints; null (omitted) on every other violation, that rule's
///     siblings included.
/// </param>
/// <param name="Detail">Kind-specific context, or null (omitted) when the kind carries none.</param>
/// <param name="GrandfatheredSiteCount">
///     How many sites this violation's baseline entry records, present only when the violation is red
///     <em>because</em> it carries more than that — a grandfathered pair that grew. Beside
///     <see cref="SiteCount" /> and declared ahead of it so the pair reads as allowance then measurement,
///     and null (omitted) everywhere else, which is every violation on a report from a spec whose
///     baselines are all still within their counts.
/// </param>
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
    string? SubjectProject,
    string? Package,
    string? Detail,
    int? GrandfatheredSiteCount,
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
