using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Baselines;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The <c>baseline</c> pipeline (DESIGN.md §5): grandfather, shrink, or deliberately grow the
///     ratcheted baselines — both Migrate rules and Freeze containment (GRAMMAR §7). Mode validation
///     runs first (before the workspace cost). <c>--init</c> captures each <em>uncaptured</em>
///     ratcheted rule's current violations (an empty section for a clean rule — "captured, zero
///     debt"). <c>--accept-reductions</c> removes captured entries whose violation no longer occurs
///     and <em>refuses</em> new ones. <c>--add</c> is the ratchet's escape valve (DESIGN.md §13(d)):
///     it grandfathers exactly one currently observed violation of a captured rule, with mandatory
///     attribution — growth is never silent, never bulk. The command never gates — it always exits 0
///     on success. Tamper (a hand-edited digest) refuses loudly with the restore hint, the same as
///     <c>check</c>. Output/error writers are injected so the e2e tests can capture them.
/// </summary>
internal sealed class BaselineRunner(TextWriter output, TextWriter error)
{
    public async Task<int> RunAsync(BaselineRequest request, CancellationToken ct)
    {
        // Mode validation FIRST — before discovering a solution or loading a workspace.
        ValidateMode(request);

        using WorkspaceModel workspace = await ModelPipeline.LoadWithWorkspaceAsync(
            request.Solution, request.Spec, request.WorkingDirectory, ct);
        foreach (string diagnostic in workspace.Diagnostics) error.WriteLine($"warning: {diagnostic}");

        IReadOnlyCollection<string>? exclude = workspace.Resolution.ExcludeProjectName is { } name ? [name] : null;
        CodebaseModel codebase = await CodebaseExtractor.ExtractFromSolutionAsync(workspace.Solution, exclude, ct);

        // Evaluate against an empty baseline so every current violation surfaces as the state to capture.
        CheckReport report = ArchChecker.Check(workspace.Model, codebase, BaselineIndex.Empty);

        // Branch to the single-rule add path before the ratchet survey below, so a bad --rule refuses instead of exiting 0.
        if (request.Add)
            return AddEntry(request, report, workspace.SolutionDirectory);

        var ratchetResults = report.Results.Where(r => r.Rule.BaselinePath is not null).ToList();
        if (ratchetResults.Count == 0)
        {
            output.WriteLine("no ratcheted rules (Migrate or Freeze containment) in the spec; nothing to do.");
            return 0;
        }

        foreach (FileGroup group in GroupByFile(ratchetResults, workspace.SolutionDirectory))
            ApplyFile(request, group, workspace.SolutionDirectory);

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
                $"rule '{ruleId}' is not ratcheted — only Migrate and Freeze containment rules carry baselines.");

        (_, bool baselinable) = CurrentEntries(result);
        if (!baselinable)
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
        string label = outcome == WriteOutcome.Wrote ? "wrote" : "unchanged";
        output.WriteLine($"{label} {PathFormat.Relative(solutionDirectory, path)}");
        return 0;
    }

    private void ApplyFile(BaselineRequest request, FileGroup group, string solutionDirectory)
    {
        // Read + verify once (tamper throws here — --init cannot distinguish tamper from corruption).
        BaselineDocument? existing = BaselineStore.TryReadDocument(group.Path);
        var sections = new Dictionary<string, IReadOnlyList<BaselineEntry>>(StringComparer.Ordinal);
        if (existing is not null)
            foreach (var section in existing.Sections)
                sections[section.Key] = section.Value; // sections for rules not in this run (e.g. a removed rule) ride through untouched

        foreach (RuleResult result in group.Rules) ApplyRule(request, result, sections);

        WriteOutcome outcome = BaselineStore.Write(group.Path, new BaselineDocument(sections));
        string label = outcome == WriteOutcome.Wrote ? "wrote" : "unchanged";
        output.WriteLine($"{label} {PathFormat.Relative(solutionDirectory, group.Path)}");
    }

    private void ApplyRule(BaselineRequest request, RuleResult result, Dictionary<string, IReadOnlyList<BaselineEntry>> sections)
    {
        string ruleId = result.Rule.Id;
        (var current, bool baselinable) = CurrentEntries(result);
        if (!baselinable)
        {
            output.WriteLine($"{ruleId}: cannot capture — the rule has an empty subject or an evaluation error; skipped.");
            return;
        }

        bool captured = sections.TryGetValue(ruleId, out var existingEntries);
        if (request.Init)
            InitRule(ruleId, current, captured, existingEntries, sections);
        else
            AcceptReductions(ruleId, current, captured, existingEntries, sections);
    }

    // --init: grandfather an uncaptured rule's current state (empty = zero debt); leave captured rules be.
    private void InitRule(
        string ruleId, IReadOnlyList<BaselineEntry> current, bool captured,
        IReadOnlyList<BaselineEntry>? existingEntries, Dictionary<string, IReadOnlyList<BaselineEntry>> sections)
    {
        if (captured)
        {
            output.WriteLine($"{ruleId}: already captured ({existingEntries!.Count} entries) — unchanged.");
            return;
        }

        sections[ruleId] = current;
        output.WriteLine($"{ruleId}: captured {current.Count} grandfathered {Plural(current.Count, "violation")}.");
    }

    // --accept-reductions: section := section ∩ current. Never adds; reports refused growth. Never gates.
    private void AcceptReductions(
        string ruleId, IReadOnlyList<BaselineEntry> current, bool captured,
        IReadOnlyList<BaselineEntry>? existingEntries, Dictionary<string, IReadOnlyList<BaselineEntry>> sections)
    {
        if (!captured)
        {
            output.WriteLine($"{ruleId}: no baseline section — run 'loadbearing baseline --init' first.");
            return;
        }

        var currentSet = new HashSet<BaselineEntry>(current);
        var existingSet = new HashSet<BaselineEntry>(existingEntries!);
        var kept = existingEntries!.Where(currentSet.Contains).ToList();
        int removed = existingEntries!.Count - kept.Count;
        int additions = current.Count(entry => !existingSet.Contains(entry));

        sections[ruleId] = kept;
        output.WriteLine(removed > 0
            ? $"{ruleId}: accepted {removed} {Plural(removed, "reduction")}."
            : $"{ruleId}: nothing to accept.");
        if (additions > 0)
            output.WriteLine(
                $"{ruleId}: refused {additions} {Plural(additions, "addition")} — a captured baseline grows only via 'loadbearing baseline --add', one attributed entry at a time.");
    }

    // A ratcheted rule's current baseline entries (from an empty-baseline check). Any EmptySubject/RuleError
    // violation makes the whole rule unbaselinable (its identity is not stable).
    private static (IReadOnlyList<BaselineEntry> Entries, bool Baselinable) CurrentEntries(RuleResult result)
    {
        var entries = new List<BaselineEntry>();
        foreach (Violation violation in result.Violations)
        {
            BaselineEntry? entry = violation.BaselineIdentity();
            if (entry is null) return (Array.Empty<BaselineEntry>(), false);
            entries.Add(entry);
        }

        return (entries, true);
    }

    private static IEnumerable<FileGroup> GroupByFile(IReadOnlyList<RuleResult> ratchetResults, string solutionDirectory)
    {
        var groups = new List<FileGroup>();
        foreach (RuleResult result in ratchetResults)
        {
            string absolutePath = BaselineStore.ResolvePath(result.Rule.BaselinePath!, solutionDirectory);
            FileGroup? group = groups.FirstOrDefault(g => string.Equals(g.Path, absolutePath, StringComparison.Ordinal));
            if (group is null)
            {
                group = new FileGroup(absolutePath);
                groups.Add(group);
            }

            group.Rules.Add(result);
        }

        return groups;
    }

    private static string Plural(int count, string noun)
    {
        return count == 1 ? noun : noun + "s";
    }

    private sealed class FileGroup(string path)
    {
        public string Path { get; } = path;
        public List<RuleResult> Rules { get; } = [];
    }
}