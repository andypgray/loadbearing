using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Baselines;

namespace Zphil.LoadBearing.Cli;

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
///     <para>Output/error writers are injected so the e2e tests can capture them.</para>
/// </remarks>
internal sealed class BaselineRunner(TextWriter output, TextWriter error, ISolutionSource? source = null)
    : WorkspaceRunner(source)
{
    public async Task<int> RunAsync(BaselineRequest request, CancellationToken ct)
    {
        // Mode validation FIRST — before discovering a solution or loading a workspace.
        ValidateMode(request);

        using var source = await CodebaseSource.CreateWithSpecAsync(
            SolutionSource, request.Solution, request.Spec, request.WorkingDirectory, ct);

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

        // Evaluate against an empty baseline so every current violation surfaces as the state to capture.
        // No narrowing goes down: this report is a capture survey rather than a verdict, the two modes that
        // read absence as evidence have already refused above, and --add branches out before the survey.
        CheckReport report = ArchChecker.Check(source.Model, codebase, BaselineIndex.Empty);

        // Branch to the single-rule add path before the ratchet survey below, so a bad --rule refuses instead of exiting 0.
        if (request.Add)
            return AddEntry(request, report, source.SolutionDirectory);

        var ratchetResults = report.Results.Where(r => r.Rule.BaselinePath is not null).ToList();
        if (ratchetResults.Count == 0)
        {
            foreach (string line in RatchetSurveyNotice.Lines(report, anyRatchetedRule: false))
                output.WriteLine(line);
            return 0;
        }

        // Ordinal grouping in first-appearance order, so two rules sharing a baseline file are read, spliced
        // and written once between them.
        var fileGroups = ratchetResults
            .GroupBy(r => BaselineStore.ResolvePath(r.Rule.BaselinePath!, source.SolutionDirectory), StringComparer.Ordinal);

        foreach (var group in fileGroups)
            ApplyFile(request, group, source.SolutionDirectory);

        // The survey's last word: name the failing rules no baseline can capture, after the per-file lines.
        foreach (string line in RatchetSurveyNotice.Lines(report, anyRatchetedRule: true))
            output.WriteLine(line);

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

        if (CurrentEntries(result) is null)
            throw new UserErrorException(
                $"cannot add to rule '{ruleId}' — the rule has an empty subject or an evaluation error.");

        string path = BaselineStore.ResolvePath(result.Rule.BaselinePath, solutionDirectory);
        BaselineDocument? existing = BaselineStore.TryReadDocument(path);
        if (existing is null || !existing.Sections.TryGetValue(ruleId, out var existingEntries))
            throw new UserErrorException($"no baseline section for '{ruleId}' — run 'loadbearing baseline --init' first.");

        Violation violation = request.Subject is not null
            ? BaselineAddMatcher.ResolveSubject(ruleId, result.Violations, request.Subject)
            : BaselineAddMatcher.ResolveEdge(ruleId, result.Violations, request.Source!, request.Target!);

        BaselineEntry identity = violation.BaselineIdentity()!;
        BaselineEntry attributed = identity.WithBecause(request.Because!);

        var sections = new Dictionary<string, IReadOnlyList<BaselineEntry>>(StringComparer.Ordinal);
        foreach (var section in existing.Sections)
            sections[section.Key] = section.Value; // co-resident foreign sections ride through untouched

        if (existingEntries.Contains(identity))
        {
            sections[ruleId] = existingEntries.Select(e => e.Equals(identity) ? attributed : e).ToList();
            output.WriteLine($"{ruleId}: entry already baselined — attribution updated.");
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

    private void ApplyFile(BaselineRequest request, IGrouping<string, RuleResult> group, string solutionDirectory)
    {
        // Read + verify once (tamper throws here — --init cannot distinguish tamper from corruption).
        BaselineDocument? existing = BaselineStore.TryReadDocument(group.Key);
        var sections = new Dictionary<string, IReadOnlyList<BaselineEntry>>(StringComparer.Ordinal);
        if (existing is not null)
            foreach (var section in existing.Sections)
                sections[section.Key] = section.Value; // sections for rules not in this run (e.g. a removed rule) ride through untouched

        foreach (RuleResult result in group) ApplyRule(request, result, sections);

        WriteOutcome outcome = BaselineStore.Write(group.Key, new BaselineDocument(sections));
        output.WriteLine(WriteReport.Line(outcome, solutionDirectory, group.Key));
    }

    private void ApplyRule(BaselineRequest request, RuleResult result, Dictionary<string, IReadOnlyList<BaselineEntry>> sections)
    {
        string ruleId = result.Rule.Id;
        if (CurrentEntries(result) is not { } current)
        {
            output.WriteLine($"{ruleId}: cannot capture — the rule has an empty subject or an evaluation error; skipped.");
            return;
        }

        sections.TryGetValue(ruleId, out var existingEntries);
        if (request.Init)
            InitRule(ruleId, current, existingEntries, sections);
        else
            AcceptReductions(ruleId, current, existingEntries, sections);
    }

    // --init: grandfather an uncaptured rule's current state (empty = zero debt); leave captured rules be.
    private void InitRule(
        string ruleId, IReadOnlyList<BaselineEntry> current,
        IReadOnlyList<BaselineEntry>? existingEntries, Dictionary<string, IReadOnlyList<BaselineEntry>> sections)
    {
        if (existingEntries is { } captured)
        {
            output.WriteLine($"{ruleId}: already captured ({captured.Count} entries) — unchanged.");
            return;
        }

        sections[ruleId] = current;
        output.WriteLine($"{ruleId}: captured {current.Count} grandfathered {Plurals.Noun(current.Count, "violation")}.");
    }

    // --accept-reductions: section := section ∩ current. Never adds; reports refused growth. A violation that
    // no longer occurs is the whole point of the mode — which is why the incomplete-model gate fires long
    // before this runs, since a project that stopped loading looks exactly like a violation that stopped.
    private void AcceptReductions(
        string ruleId, IReadOnlyList<BaselineEntry> current,
        IReadOnlyList<BaselineEntry>? existingEntries, Dictionary<string, IReadOnlyList<BaselineEntry>> sections)
    {
        if (existingEntries is not { } captured)
        {
            output.WriteLine($"{ruleId}: no baseline section — run 'loadbearing baseline --init' first.");
            return;
        }

        var currentSet = new HashSet<BaselineEntry>(current);
        var existingSet = new HashSet<BaselineEntry>(captured);
        var kept = captured.Where(currentSet.Contains).ToList();
        int removed = captured.Count - kept.Count;
        int additions = current.Count(entry => !existingSet.Contains(entry));

        sections[ruleId] = kept;
        output.WriteLine(removed > 0
            ? $"{ruleId}: accepted {removed} {Plurals.Noun(removed, "reduction")}."
            : $"{ruleId}: nothing to accept.");
        if (additions > 0)
            output.WriteLine(
                $"{ruleId}: refused {additions} {Plurals.Noun(additions, "addition")} — a captured baseline grows only via 'loadbearing baseline --add', one attributed entry at a time.");
    }

    // A ratcheted rule's current baseline entries (from an empty-baseline check), or null when the rule is
    // unbaselinable: any EmptySubject/RuleError violation makes the whole rule so (its identity is not stable).
    private static IReadOnlyList<BaselineEntry>? CurrentEntries(RuleResult result)
    {
        var entries = new List<BaselineEntry>();
        foreach (Violation violation in result.Violations)
        {
            BaselineEntry? entry = violation.BaselineIdentity();
            if (entry is null) return null;
            entries.Add(entry);
        }

        return entries;
    }
}
