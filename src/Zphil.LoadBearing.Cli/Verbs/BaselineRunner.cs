using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Cli.Mcp.Infrastructure;
using Zphil.LoadBearing.Cli.Pipeline;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Baselines;
using Zphil.LoadBearing.Roslyn.Diagnostics;

namespace Zphil.LoadBearing.Cli.Verbs;

/// <summary>
///     The <c>baseline</c> pipeline: grandfather, shrink, or deliberately grow the
///     ratcheted baselines — both Migrate rules and Quarantine containment (GRAMMAR §7). Mode validation
///     runs first (before the workspace cost).
/// </summary>
/// <remarks>
///     <para>
///         <b>The three modes.</b> <c>--init</c> captures each <em>uncaptured</em> ratcheted rule's current
///         violations (an empty section for a clean rule — "captured, zero debt").
///         <c>--accept-reductions</c> removes captured entries whose violation no longer occurs and
///         <em>refuses</em> new ones. Both modes end by naming any rules failing with no baseline to
///         capture — the reds the ratchet cannot absorb. <c>--add</c> is the ratchet's escape valve: it
///         grandfathers exactly one currently observed violation of a captured rule, with mandatory
///         attribution — growth is never silent, never bulk.
///     </para>
///     <para>
///         <b>The site measure rides the same three verbs</b> (GRAMMAR §4.3), and the direction of each is
///         what decides how. Every write records what it observed on the edge entries it writes, so
///         <c>--init</c> and <c>--add</c> carry counts by construction. <c>--accept-reductions</c> may only
///         tighten: it records a count on an entry that had none and lowers one that came in over, and it
///         refuses a rise in the same breath it refuses a new entry — because a rise <em>is</em> new debt.
///         <c>--add</c> is therefore the only route by which a count goes up, which is what keeps growth
///         attributed. One arm of that follows from the format rather than the verbs:
///         <c>--init</c> declines to write a file it captured nothing into, so it can never upgrade a
///         legacy file's schema version while reporting it unchanged.
///     </para>
///     <para>
///         <b>What it refuses.</b> Tamper (a hand-edited digest) refuses loudly with the restore hint, the
///         same as <c>check</c>. A workspace-load failure refuses the whole command on <c>check</c>'s
///         terms — exit 2, nothing written, opt-out
///         <see cref="BaselineRequest.AllowWorkspaceDiagnostics" />
///         (<see cref="IncompleteModelGate" />). The gate fires before extraction and so before any mode
///         does its work, because every mode is dangerous against a partial model: <c>--init</c> would
///         capture "zero debt" for rules whose subjects live in projects that did not load, and
///         <c>--accept-reductions</c> would delete real entries as violations that "no longer occur" when
///         the only thing that stopped is a project loading. A solution filter that left declared projects
///         unchecked refuses in the same position and on the same reasoning
///         (<see cref="NarrowedUniverseNotice.BaselineRefusal" />), but only for those two modes:
///         <c>--add</c> records one violation the run did see and claims nothing about what it did not.
///         Short of that the command reports rather than gates — a red rule is the state to capture, not a
///         failure, so it exits 0 on success.
///     </para>
///     <para>
///         <b>The persisted extraction cache is fronted by <c>--add</c> alone</b>, and the split falls out
///         of the same sentence: a stale cache is a third route to the smaller-than-real model the two gates
///         above refuse <c>--init</c> and <c>--accept-reductions</c> on — and the only one of the three that
///         is silent, because a hit replays the recorded diagnostics and so neither gate can see it. Those
///         two modes therefore extract cache-free whatever <c>--no-cache</c> says, while <c>--add</c> takes
///         the operator's answer.
///     </para>
///     <para>Output/error writers are injected so the e2e tests can capture them.</para>
/// </remarks>
internal sealed class BaselineRunner(
    TextWriter output,
    TextWriter error,
    ISolutionSource? source = null,
    IEnvironment? environment = null)
    : CacheWiredRunner(source, environment)
{
    public async Task<int> RunAsync(BaselineRequest request, CancellationToken ct)
    {
        // Mode validation FIRST — before discovering a solution or loading a workspace.
        ValidateMode(request);

        // --init and --accept-reductions read absence as evidence, so they extract cache-free: a stale hit is
        // a smaller-than-real model, and unlike a failed load or a solution filter it raises no diagnostic the
        // two gates below could refuse on. --add records one violation the run did see, so it may front the
        // cache and the operator's --no-cache is the whole policy there.
        bool readsAbsenceAsEvidence = request.Init || request.AcceptReductions;
        using var source = await CodebaseSource.CreateWithSpecAsync(
            SolutionSource, Environment, request.Solution, request.Spec, request.WorkingDirectory,
            readsAbsenceAsEvidence || request.NoCache, ct);

        // Composed for the render, so the MSBuild-selection note goes out with the load failures.
        WorkspaceDiagnostics diagnostics = source.Diagnostics;
        WorkspaceDiagnosticsRenderer.Render(error, diagnostics.Rendered);

        // Fail closed before extraction, and so before any mode writes a byte: the workspace's own load
        // failures are the whole gate input here (merge notes are minted later, inside extraction), filtered
        // for NuGetAudit advisories exactly as check filters them.
        if (IncompleteModelNotices.Refused(
                error, diagnostics, request.AllowWorkspaceDiagnostics, IncompleteModelGate.BaselineMessage))
            return 2;

        // Then the narrowing refusal, on the gate's own terms and in the same position — before extraction,
        // so nothing is written. Both modes below read absence as evidence: --init captures "zero debt" for
        // rules whose subjects the filter left out, and --accept-reductions deletes real entries as
        // violations that stopped occurring. --add rides through: it records what the run did see.
        if ((request.Init || request.AcceptReductions) && diagnostics.IsNarrowed)
        {
            NarrowingNotices.Refusal(error, source, NarrowedUniverseNotice.BaselineRefusal);
            return 2;
        }

        CodebaseModel codebase = await source.ExtractAsync(source.Resolution.ExcludeProjectNames, ct);
        RecordCacheOutcome(source);

        // Evaluate against an empty baseline so every current violation surfaces as the state to capture.
        // No narrowing goes down: this report is a capture survey rather than a verdict, the two modes that
        // read absence as evidence have already refused above, and --add branches out before the survey.
        CheckReport report = ArchChecker.Check(source.Model, codebase, BaselineIndex.Empty);

        // Branch to the single-rule add path before the ratchet survey below, so a bad --rule refuses instead of exiting 0.
        if (request.Add)
            return AddEntry(request, report, source.SolutionDirectory);

        List<RuleResult> ratchetResults = report.Results.Where(r => r.Rule.BaselinePath is not null).ToList();
        if (ratchetResults.Count == 0)
        {
            foreach (string line in RatchetSurveyNotice.Lines(report, anyRatchetedRule: false))
                await output.WriteLineAsync(line);
            return 0;
        }

        // The directory is read out before the apply below, because the grouping in there is lazy: a lambda
        // closing over `source` would outlive the using block if the sequence were ever enumerated later.
        string solutionDirectory = source.SolutionDirectory;
        ApplyFiles(request, ratchetResults, solutionDirectory);

        // The survey's last word: name the failing rules no baseline can capture, after the per-file lines.
        foreach (string line in RatchetSurveyNotice.Lines(report, anyRatchetedRule: true))
            await output.WriteLineAsync(line);

        return 0;
    }

    // Mode + companion validation: exactly one mode, and the --add companions are coherent — all before the workspace cost.
    private static void ValidateMode(BaselineRequest request)
    {
        int modes = (request.Init ? 1 : 0) + (request.AcceptReductions ? 1 : 0) + (request.Add ? 1 : 0);
        if (modes != 1)
            throw new UserErrorException("Specify exactly one of --init, --accept-reductions, or --add.");

        if (!request.Add && (request.Rule is not null || request.Because is not null
                                                      || request.Source is not null || request.Target is not null || request.Subject is not null))
            throw new UserErrorException("--rule, --because, --source, --target, and --subject apply only with --add.");

        if (!request.Add) return;

        if (request.Rule is null)
            throw new UserErrorException("--add requires --rule <id>.");

        bool blankOrMultiline = string.IsNullOrWhiteSpace(request.Because)
                                || request.Because.IndexOf('\r') >= 0
                                || request.Because.IndexOf('\n') >= 0;
        if (blankOrMultiline)
            throw new UserErrorException("--add requires a non-blank, single-line --because.");

        bool edgeForm = request.Source is not null && request.Target is not null && request.Subject is null;
        bool subjectForm = request.Subject is not null && request.Source is null && request.Target is null;
        if (!edgeForm && !subjectForm)
            throw new UserErrorException(
                "--add requires exactly one entry form: --source with --target (an edge), or --subject (a shape).");
    }

    // --add: append one attributed entry to a captured rule's section (or update attribution on a present
    // entry). Internal so the fast-tier runner tests drive it over an in-memory report (no workspace).
    internal int AddEntry(BaselineRequest request, CheckReport report, string solutionDirectory)
    {
        string ruleId = request.Rule!;
        RuleResult? result = report.Results.FirstOrDefault(r => string.Equals(r.Rule.Id, ruleId, StringComparison.Ordinal));
        if (result is null)
            throw new UserErrorException($"rule '{ruleId}' is not in the spec.");
        if (result.Rule.BaselinePath is null)
            throw new UserErrorException(
                $"rule '{ruleId}' is not ratcheted — only Migrate and Quarantine containment rules carry baselines.");

        if (CurrentEntries(result) is not { } current)
            throw new UserErrorException(
                $"cannot add to rule '{ruleId}' — the rule has an empty subject or an evaluation error.");

        string path = BaselineStore.ResolvePath(result.Rule.BaselinePath, solutionDirectory);
        BaselineDocument? existing = BaselineStore.TryReadDocument(path);
        if (existing is null || !existing.Sections.TryGetValue(ruleId, out IReadOnlyList<BaselineEntry>? existingEntries))
            throw new UserErrorException($"no baseline section for '{ruleId}' — run 'loadbearing baseline --init' first.");

        Violation violation = request.Subject is not null
            ? BaselineAddMatcher.ResolveSubject(ruleId, result.Violations, request.Subject)
            : BaselineAddMatcher.ResolveEdge(ruleId, result.Violations, request.Source!, request.Target!);

        BaselineEntry identity = violation.BaselineIdentity()!;
        // The measure comes off the folded survey rather than off the resolved violation: the matcher hands
        // back the first candidate of an identity, and where two violations share one the allowance this
        // valve records has to cover the larger. --add is the only route by which a count may rise, so
        // recording the whole observed figure here is what makes growth attributable rather than silent.
        BaselineEntry recorded = current.First(entry => entry.Equals(identity));
        BaselineEntry attributed = recorded.WithBecause(request.Because!);

        var sections = new Dictionary<string, IReadOnlyList<BaselineEntry>>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, IReadOnlyList<BaselineEntry>> section in existing.Sections)
            sections[section.Key] = section.Value; // co-resident foreign sections ride through untouched

        if (existingEntries.FirstOrDefault(entry => entry.Equals(identity)) is { } stored)
        {
            sections[ruleId] = existingEntries.Select(e => e.Equals(identity) ? attributed : e).ToList();
            output.WriteLine($"{ruleId}: entry already baselined — {ReRecorded(stored, attributed)}");
        }
        else
        {
            sections[ruleId] = existingEntries.Append(attributed).ToList();
            // The shared full-name form covers every kind the matcher resolves — subject, reference edge,
            // and member use (whose Target slot is null; the member display renders instead).
            output.WriteLine(
                $"{ruleId}: added 1 grandfathered entry — {BaselineAddMatcher.FullNameForm(violation)} (because: {request.Because}).");
        }

        WriteOutcome outcome = BaselineStore.Write(path, new BaselineDocument(sections));
        output.WriteLine(WriteReport.Line(outcome, solutionDirectory, path));
        return 0;
    }

    // The tail of the already-baselined echo. --add is the ratchet's only growth valve, so when it moves a
    // site count it says so and names both ends of the move — an operator who meant only to restate the
    // attribution should be able to read that the allowance went up. An entry with no measure to move keeps
    // the plain sentence: a subject entry carries none, and neither does an edge with no sited evidence.
    private static string ReRecorded(BaselineEntry stored, BaselineEntry attributed)
    {
        if (attributed.SiteCount is not { } observed) return "attribution updated.";

        string from = stored.SiteCount is { } previous ? $"{previous}" : "uncounted";
        return $"attribution updated and site count re-recorded ({from} → {observed}).";
    }

    // --init and --accept-reductions, one baseline file at a time: ordinal grouping in first-appearance order,
    // so two rules sharing a file are read, spliced and written once between them. Internal so the fast-tier
    // runner tests can drive both modes over an in-memory report the way they drive --add — no workspace.
    internal void ApplyFiles(BaselineRequest request, IEnumerable<RuleResult> ratchetResults, string solutionDirectory)
    {
        IEnumerable<IGrouping<string, RuleResult>> fileGroups = ratchetResults
            .GroupBy(r => BaselineStore.ResolvePath(r.Rule.BaselinePath!, solutionDirectory), StringComparer.Ordinal);

        foreach (IGrouping<string, RuleResult> group in fileGroups)
            ApplyFile(request, group, solutionDirectory);
    }

    private void ApplyFile(BaselineRequest request, IGrouping<string, RuleResult> group, string solutionDirectory)
    {
        // Read + verify once (tamper throws here — --init cannot distinguish tamper from corruption).
        BaselineDocument? existing = BaselineStore.TryReadDocument(group.Key);
        var sections = new Dictionary<string, IReadOnlyList<BaselineEntry>>(StringComparer.Ordinal);
        if (existing is not null)
            foreach (KeyValuePair<string, IReadOnlyList<BaselineEntry>> section in existing.Sections)
                sections[section.Key] = section.Value; // sections for rules not in this run (e.g. a removed rule) ride through untouched

        var rewrote = false;
        foreach (RuleResult result in group) rewrote |= ApplyRule(request, result, sections);

        // --init writes nothing to a file whose every visited section was already captured. Without that arm
        // the write still lands, and since a composed file always carries the current schema version an
        // untouched legacy file would be upgraded in place while each section echoed "already captured —
        // unchanged": the one word the operator reads would be the one thing that was not true. The other two
        // modes are the upgrade path and write regardless — both have something of their own to record.
        if (request.Init && existing is not null && !rewrote)
        {
            output.WriteLine(WriteReport.Line(WriteOutcome.Unchanged, solutionDirectory, group.Key));
            return;
        }

        // A file that does not exist and gained nothing is not written either, in any mode: --init lands
        // here only when every rule in the group was unbaselinable, --accept-reductions whenever the rule it
        // was asked to tighten was never captured — and each has just said so on its own line. A write here
        // would leave a section-less file in the tree under a "wrote" line contradicting that sentence.
        if (existing is null && !rewrote) return;

        WriteOutcome outcome = BaselineStore.Write(group.Key, new BaselineDocument(sections));
        output.WriteLine(WriteReport.Line(outcome, solutionDirectory, group.Key));
    }

    // Whether this rule rewrote its section — what tells the caller above whether the file has anything new to say.
    private bool ApplyRule(BaselineRequest request, RuleResult result, Dictionary<string, IReadOnlyList<BaselineEntry>> sections)
    {
        string ruleId = result.Rule.Id;
        if (CurrentEntries(result) is not { } current)
        {
            output.WriteLine($"{ruleId}: cannot capture — the rule has an empty subject or an evaluation error; skipped.");
            return false;
        }

        sections.TryGetValue(ruleId, out IReadOnlyList<BaselineEntry>? existingEntries);
        return request.Init
            ? InitRule(ruleId, current, existingEntries, sections)
            : AcceptReductions(ruleId, current, existingEntries, sections);
    }

    // --init: grandfather an uncaptured rule's current state (empty = zero debt); leave captured rules be.
    private bool InitRule(
        string ruleId, IReadOnlyList<BaselineEntry> current,
        IReadOnlyList<BaselineEntry>? existingEntries, Dictionary<string, IReadOnlyList<BaselineEntry>> sections)
    {
        if (existingEntries is { } captured)
        {
            output.WriteLine($"{ruleId}: already captured ({captured.Count} entries) — unchanged.");
            return false;
        }

        sections[ruleId] = current;
        output.WriteLine($"{ruleId}: captured {current.Count} grandfathered {Plurals.Noun(current.Count, "violation")}.");
        return true;
    }

    // --accept-reductions: section := section ∩ current, then each surviving entry's measure ratcheted down
    // to what this run observed. Never adds and never raises a count; reports both refusals. A violation that
    // no longer occurs is the whole point of the mode — which is why the incomplete-model gate fires long
    // before this runs, since a project that stopped loading looks exactly like a violation that stopped.
    // Recording a count on an entry that had none is a tightening too, and it is the route by which a file
    // written before the measure existed becomes a counted one.
    private bool AcceptReductions(
        string ruleId, IReadOnlyList<BaselineEntry> current,
        IReadOnlyList<BaselineEntry>? existingEntries, Dictionary<string, IReadOnlyList<BaselineEntry>> sections)
    {
        if (existingEntries is not { } captured)
        {
            output.WriteLine($"{ruleId}: no baseline section — run 'loadbearing baseline --init' first.");
            return false;
        }

        // Keyed by identity, so a captured entry's current twin — the one carrying what this run observed —
        // is one lookup away. CurrentEntries folded duplicate identities, so the keys are unique.
        Dictionary<BaselineEntry, BaselineEntry> observed = current.ToDictionary(entry => entry);
        var existingSet = new HashSet<BaselineEntry>(captured);

        var kept = new List<BaselineEntry>();
        var lowered = 0;
        var recorded = 0;
        var grew = 0;
        foreach (BaselineEntry entry in captured)
        {
            // Identity intersection first, exactly as before: an entry no current violation matches is the
            // reduction being accepted, and it simply does not survive into the section.
            if (!observed.TryGetValue(entry, out BaselineEntry? now)) continue;

            if (now.SiteCount is not { } sites)
            {
                kept.Add(entry); // nothing to measure — a subject entry, or an edge with no sited evidence
            }
            else if (entry.SiteCount is not { } allowance)
            {
                kept.Add(entry.WithSiteCount(sites));
                recorded++;
            }
            else if (sites < allowance)
            {
                kept.Add(entry.WithSiteCount(sites));
                lowered++;
            }
            else
            {
                // At or above the allowance the entry is stored untouched. Above it, the mode's direction is
                // the whole reason: raising a count is growth, and growth is attributed or it does not happen.
                if (sites > allowance) grew++;
                kept.Add(entry);
            }
        }

        int removed = captured.Count - kept.Count;
        int additions = current.Count(entry => !existingSet.Contains(entry));

        sections[ruleId] = kept;
        // "nothing to accept" is the whole mode's verdict, so every kind of acceptance clears it: a run that
        // lowered or first recorded a count tightened the baseline as surely as one that removed an entry.
        if (removed + lowered + recorded == 0) output.WriteLine($"{ruleId}: nothing to accept.");
        if (removed > 0)
            output.WriteLine($"{ruleId}: accepted {removed} {Plurals.Noun(removed, "reduction")}.");
        if (lowered > 0)
            output.WriteLine($"{ruleId}: lowered the site count on {lowered} {Plurals.Noun(lowered, "entry")}.");
        if (recorded > 0)
            output.WriteLine($"{ruleId}: recorded the site count on {recorded} {Plurals.Noun(recorded, "entry")}.");
        if (additions > 0)
            output.WriteLine(
                $"{ruleId}: refused {additions} {Plurals.Noun(additions, "addition")} — a captured baseline grows only via 'loadbearing baseline --add', one attributed entry at a time.");
        if (grew > 0)
            output.WriteLine(
                $"{ruleId}: refused site growth on {grew} {Plurals.Noun(grew, "entry")} — a grandfathered pair grows only via 'loadbearing baseline --add', one attributed entry at a time.");

        return true;
    }

    // A ratcheted rule's current baseline entries (from an empty-baseline check), or null when the rule is
    // unbaselinable: any EmptySubject/RuleError violation makes the whole rule so (its identity is not stable).
    // Each edge entry records the sites observed under its identity, max-folded across duplicates: a symbol ID
    // names a name rather than a node (GRAMMAR §4.3), so two violations can share one identity, and the
    // allowance a write records has to cover the larger of them. Folding is also what collapses those
    // duplicates to the single line the file has always meant them to be.
    private static IReadOnlyList<BaselineEntry>? CurrentEntries(RuleResult result)
    {
        var observed = new Dictionary<BaselineEntry, int>();
        var order = new List<BaselineEntry>();

        foreach (Violation violation in result.Violations)
        {
            BaselineEntry? identity = violation.BaselineIdentity();
            if (identity is null) return null;

            if (!observed.TryGetValue(identity, out int sites)) order.Add(identity);
            observed[identity] = Math.Max(sites, violation.Sites.Count);
        }

        return order.Select(identity => Counted(identity, observed[identity])).ToList();
    }

    // The entry as a write records it: an edge entry carrying the sites observed under its identity, or the
    // bare identity where there is no measure to take. A subject entry never carries one — its sites are
    // declarations (GRAMMAR §4.3) — and neither does an edge whose evidence carries no file:line at all,
    // which is the same state as an entry written before the measure existed: grandfathered at pair grain.
    private static BaselineEntry Counted(BaselineEntry identity, int sites)
    {
        return identity.IsEdge && sites > 0 ? identity.WithSiteCount(sites) : identity;
    }
}
