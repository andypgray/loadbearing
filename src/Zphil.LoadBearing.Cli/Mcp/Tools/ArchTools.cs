using System.ComponentModel;
using ModelContextProtocol.Server;
using Zphil.LoadBearing.Cli.Mcp.Pipeline;
using Zphil.LoadBearing.Cli.Pipeline;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Cli.Verbs;

namespace Zphil.LoadBearing.Cli.Mcp.Tools;

/// <summary>
///     The MCP tool surface: the five <c>arch_*</c> tools, each a thin shell that runs the
///     same internal runner its CLI verb uses against the bound solution + spec, captures stdout into a
///     <see cref="StringWriter" />, and returns the text — so CLI and MCP output are identical by
///     construction (pinned by <c>CliMcpParityTests</c>).
/// </summary>
/// <remarks>
///     Violations are data, never tool errors. Tool methods never <c>try/catch</c>: they throw, and
///     <see cref="GlobalCallToolFilter" /> shapes any <see cref="Roslyn.UserErrorException" /> or
///     spec-validation failure into an error result.
///     Reaching a Roslyn workspace type only through the runners keeps the MSBuildLocator JIT quarantine
///     intact — these methods are first JITted at the first tool call, after registration has run. Every
///     runner is handed the injected <see cref="ISolutionSource" /> so tool calls acquire the solution the
///     same way: warm (a session reconciled across calls) by default, or cold when the warm workspace is
///     disabled — the CLI's own default source. The injected <see cref="IResponseFitter" /> is how a
///     document-shaped tool answers a client whose channel is smaller than the answer: the runner offers its
///     coarsening ladder and the fitter takes the first rung that fits, rather than a tool reading process
///     state to decide.
/// </remarks>
[McpServerToolType]
internal sealed class ArchTools(McpServerBinding binding, ISolutionSource source, IResponseFitter fitter)
{
    private const string CheckDescription =
        "Run the architecture spec against the bound solution and return the JSON check report " +
        "(schemaVersion 3): rules[] keyed by id, plus summary counts. Violations are data — a red rule is a " +
        "finding, not an error. The rules parameter narrows what is evaluated, and the report then covers " +
        "only those. " +
        "Narrow with overview, skeleton or index (coarser grain) or rules (fewer rules); an over-budget " +
        "report coarsens its own grain, as far as index, rather than being cut — and index lists every rule " +
        "ID, which is what rules globs match. " +
        "subjectTypes/subjectGeneratedTypes appear together on a rule whose subject contained generator " +
        "output, pass or fail: how many types it swept and how many nobody wrote. Both absent means none " +
        "were. Narrowing the rule with .Authored() removes them. " +
        "If projects fail to load, or their NuGet packages did not resolve, the report still " +
        "returns, stamped modelIncomplete: true and failedProjects/restoreFailedProjects — a verdict reached " +
        "against a partial model; report that, never plain green. " +
        "Under a .slnf solution filter, uncheckedProjects names the declared projects the run never " +
        "checked — a clean report then covers a subset; say so. " +
        "unsupportedProjects names the declared projects this product cannot read at all (C# only), each " +
        "with its reason — no rule can be violated in one, so a clean report over a polyglot solution is " +
        "not a clean solution either. " +
        "multiTargetedProjects names the projects that compiled for several frameworks, each with the " +
        "framework its shared types' facts follow — what another framework's #if guards was never in the " +
        "model, so a clean report over such a project covers one framework's view of it.";

    private const string StatusDescription =
        "Return the JSON migration burndown (schemaVersion 2): per-rule grandfathered/stale counts and " +
        "promotion suggestions. If projects fail to load, or their NuGet packages did not resolve, the " +
        "burndown still returns, stamped modelIncomplete: true and failedProjects/restoreFailedProjects — " +
        "counts from a partial model; report that rather than quoting them as whole. " +
        "Under a .slnf solution filter, uncheckedProjects names the declared projects the run never checked; " +
        "they contribute no violations, so every count reads low. unsupportedProjects names the declared " +
        "projects this product cannot read at all (C# only): their zero is permanent, not burndown.";

    private const string ExplainDescription =
        "Return one rule's because, fix, posture payload, and linked prose as text.";

    private const string ContextDescription =
        "Return the architecture scope card(s) covering a path — a quarantined scope's dragons + sanctioned surface, " +
        "or a layer's local rules — or a pointer line when none apply. If projects fail to load or to restore, the " +
        "answer opens with a caveat naming them: cards from unloaded projects cannot be placed, and a rule about " +
        "a package the restore never fetched was never measured, so treat a no-coverage answer as unproven " +
        "there. A .slnf solution filter gets the same caveat for the declared projects it left " +
        "unchecked: treat a no-coverage answer as unproven under them as well.";

    private const string GraphDescription =
        "Return the JSON codebase survey (schemaVersion 1): projects[] with namespace inventories, " +
        "projectEdges[] (source/target, declared vs observed dependencies), and externalEdges[] grouped by " +
        "namespace root. " +
        "Every project the workspace loaded is here, which is wider than the solution: solutionMember false " +
        "marks a passenger a ProjectReference dragged in, and an absent key means membership was unreadable. " +
        "projectEdges[] carry only references the code declares: where one source file is compiled into " +
        "several projects, a project referencing its own compiled-in copy is not an edge to the declarer the " +
        "type was attributed to, and multiplyDeclaredTypes[] names those types with every declaring project " +
        "and the one whose facts won — arch.Project() on any declarer selects them, with rule verdicts " +
        "answering from the winner's facts (the key is " +
        "absent when the solution has none). shadowedTypes[] is the other coverage key: a full name a project " +
        "declares that a referenced assembly also supplies means two types, so a rule naming it reaches both " +
        "while arch.Project() over the declaring project reaches only the declaration. " +
        "generated qualifies the types count on a project and on each namespace (absent when zero): how many " +
        "of them a generator emitted, which is what arch.Project() would sweep and .Authored() would drop. A " +
        "namespace whose two counts are equal is wholly generator output — never make it a layer glob. " +
        "targetFrameworks rides every project's row and names what it declares, normalized (a project " +
        "predating the SDK reads net48); factsFollow rides only a multi-targeted one and names the " +
        "framework its shared types' facts came from — what another framework's #if guards is not in the " +
        "model. isPackable, locksPackages and packageReferences are the artifact facts, evaluated rather " +
        "than read off the project file, so a value an SDK defaults or a props file above the solution sets " +
        "is the one reported; packageReferences is what the project declares, never the transitive closure. " +
        "Needs no spec — call it before one exists to plan layers and rules. Needs the solution restored and " +
        "built: if projects fail to load, or their NuGet packages did not resolve, it returns an error naming " +
        "them rather than a survey missing them or missing their external edges. " +
        "Narrow with overview, skeleton or index (coarser grain) or projects (fewer projects); an " +
        "over-budget survey coarsens its own grain, as far as index, rather than being cut — and index lists " +
        "every project name, which is what projects globs match. " +
        "Under a .slnf solution filter, uncheckedProjects names the declared projects the run never loaded — " +
        "a project absent from the survey may simply be out of view. unsupportedProjects is the third " +
        "coverage key and the one that bounds the survey's subject: the declared projects this product " +
        "cannot read at all (C# only), each with its reason, so projects[] shorter than the solution is a " +
        "stated fact rather than something to infer by opening the solution file.";

    [McpServerTool(
        Name = ArchToolNames.Check,
        Title = "Architecture Check",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description(CheckDescription)]
    public async Task<string> CheckAsync(
        [Description("Git ref; files changed since it that fall in a quarantined scope raise a tripwire warning.")]
        string? diffBase = null,
        [Description(
            "Rule-ID globs as one string, not an array, semicolon-separated ('*' spans '/'). Only matching "
            + "rules run, so rules[] and summary cover that subset alone. Matching no rule is an error "
            + "listing the available IDs.")]
        string? rules = null,
        [Description(
            "Elide each violation's sites, keeping every rule and every violation; siteCount (always "
            + "present) still says how many. Coarser grain, never a narrower subject.")]
        bool overview = false,
        [Description(
            "Elide the violations too, keeping every rule with its verdict, prose, baseline and warnings; "
            + "violationCount (always present) still says how many. Coarser than overview, still not a "
            + "narrower subject.")]
        bool skeleton = false,
        [Description(
            "Elide the rule prose too — sentence, because and fix — and the workspaceDiagnostics stream "
            + "(workspaceDiagnosticCount says how many), keeping every rule's id, posture, status, "
            + "baseline, warnings and violationCount, and every trust stamp. The coarsest grain, and the "
            + "menu the rules parameter picks from; arch_explain returns any of these ids whole. Still not "
            + "a narrower subject.")]
        bool index = false,
        CancellationToken cancellationToken = default)
    {
        // Exit code and error writer deliberately discarded — everything they would carry is in the document.
        // Violations ride in rules[]; which projects failed to load or to restore rides in failedProjects and
        // restoreFailedProjects, and the load's
        // own diagnostics ride in workspaceDiagnostics along with the MSBuild-selection note, which
        // WorkspaceDiagnostics puts in the list both surfaces read rather than appending at write time (it
        // was the one line TextWriter.Null used to swallow, and "which MSBuild opened it" is the next
        // question after any load failure). The gate verdict the exit code would have expressed rides in
        // modelIncomplete, and an unmatched --rules filter surfaces as an error result rather than exit 2.
        //
        // The fitter carries this surface's response budget, as it does for arch_graph: over it, the report
        // comes back whole at a coarser grain instead of cut mid-array. It matters more here than there —
        // this is the tool agents are told to call before finishing work, and its bulk driver is a per-site
        // dump with no ceiling, so a legacy migration burndown scales it without limit.
        return await CaptureAsync(output => new CheckRunner(output, TextWriter.Null, source, fitter: fitter)
            .RunAsync(
                binding.CheckRequest(diffBase, rules, DocumentGrains.Coarsest(overview, skeleton, index)),
                cancellationToken));
    }

    [McpServerTool(
        Name = ArchToolNames.Status,
        Title = "Architecture Status",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description(StatusDescription)]
    public async Task<string> StatusAsync(CancellationToken cancellationToken = default)
    {
        return await CaptureAsync(output => new StatusRunner(output, TextWriter.Null, source)
            .RunAsync(binding.StatusRequest(), cancellationToken));
    }

    [McpServerTool(
        Name = ArchToolNames.Explain,
        Title = "Architecture Explain",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description(ExplainDescription)]
    public async Task<string> ExplainAsync(
        [Description("A post-desugar rule ID, e.g. layering/domain-independent or legacy/billing/containment.")]
        string ruleId,
        CancellationToken cancellationToken = default)
    {
        // Error writer deliberately discarded: explain's answer is spec-derived and cannot be made wrong by
        // a load failure; the caveat channel for a partial model is arch_context's body, not this tool's.
        return await CaptureAsync(output => new ExplainRunner(output, TextWriter.Null, source)
            .RunAsync(binding.ExplainRequest(ruleId), cancellationToken));
    }

    [McpServerTool(
        Name = ArchToolNames.Context,
        Title = "Architecture Context",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description(ContextDescription)]
    public async Task<string> ContextAsync(
        [Description(
            "A file or directory path (absolute or solution-relative) as one string, not an array, to find "
            + "architecture scope cards for. A path no card covers — including one the codebase does not "
            + "contain yet — answers successfully saying so; a JSON array written as text is an error naming "
            + "that shape rather than a no-coverage answer that was never asked the question.")]
        string path,
        CancellationToken cancellationToken = default)
    {
        // No error writer to discard: context's body is its only channel, so its runner takes stdout alone.
        return await CaptureAsync(output => new ContextRunner(output, source)
            .RunAsync(binding.ContextRequest(path), cancellationToken));
    }

    [McpServerTool(
        Name = ArchToolNames.Graph,
        Title = "Architecture Graph",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description(GraphDescription)]
    public async Task<string> GraphAsync(
        [Description(
            "Survey the partial model even when some projects fail to load; otherwise the call returns an "
            + "error naming what failed, because a survey missing whole projects is a wrong map, not a smaller one.")]
        bool allowWorkspaceDiagnostics = false,
        [Description(
            "Elide every project's namespace inventory, keeping every project, edge and external row. Coarser "
            + "grain, never a narrower subject.")]
        bool overview = false,
        [Description(
            "Elide the namespace inventories, the external-reference rows, the multiply-declared types and the "
            + "shadowed names, "
            + "keeping every project, its declared references and type count, and the observed project "
            + "edges; each elided section is reported as a count. Coarser than overview, still not a "
            + "narrower subject.")]
        bool skeleton = false,
        [Description(
            "Elide each project's declared references and target frameworks too, the observed project edges "
            + "with them (projectEdgeCount says how many), and the workspaceDiagnostics stream "
            + "(workspaceDiagnosticCount likewise), keeping every project's name, solution membership and "
            + "type count, and every trust stamp. The coarsest grain, and the menu the projects parameter "
            + "picks from. Still not a narrower subject.")]
        bool index = false,
        [Description(
            "Project-name globs as one string, not an array, semicolon-separated ('*' allowed). References "
            + "in both directions are kept, so an edge can name a project outside the scope. Matching no "
            + "project is an error listing the available names.")]
        string? projects = null,
        CancellationToken cancellationToken = default)
    {
        // Unlike arch_check and arch_status, the incomplete-model verdict cannot ride this document by
        // default — graph refuses before there is one — so the refusal throws and GlobalCallToolFilter
        // returns it as a clean un-logged error result.
        //
        // The response budget is this surface's alone, and the fitter is what carries it: over budget, the
        // ladder the runner offers is walked until a whole document fits instead of handing the truncator one
        // to cut in half. One extraction, and the degraded answer is byte-identical to an explicit call at the
        // grain it landed on, so a reader can trust the grain stamp rather than diffing two surveys. The cap
        // is the truncator's, so degrading fires against exactly the number that would otherwise have
        // truncated.
        return await CaptureAsync(output => new GraphRunner(output, TextWriter.Null, source, fitter: fitter)
            .RunAsync(
                binding.GraphRequest(
                    allowWorkspaceDiagnostics, DocumentGrains.Coarsest(overview, skeleton, index), projects),
                cancellationToken));
    }

    // Every tool's shell: give the run a stdout to write into, discard the exit code it returns, and answer
    // with what it wrote. Only BCL types appear here, so a runner is still reached solely from inside a tool
    // method's own body and the MSBuildLocator JIT quarantine is exactly as tight as it was.
    private static async Task<string> CaptureAsync(Func<TextWriter, Task<int>> run)
    {
        var output = new StringWriter();
        await run(output);
        return output.ToString();
    }
}
