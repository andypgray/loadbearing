using System.ComponentModel;
using ModelContextProtocol.Server;
using Zphil.LoadBearing.Cli.Mcp.Infrastructure;
using Zphil.LoadBearing.Cli.Mcp.Pipeline;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Roslyn;

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
///     disabled — the CLI's own default source. The injected <see cref="IEnvironment" /> is how a tool
///     learns the client's response budget without reading process state directly.
/// </remarks>
[McpServerToolType]
internal sealed class ArchTools(McpServerBinding binding, ISolutionSource source, IEnvironment environment)
{
    private const string CheckDescription =
        "Run the architecture spec against the bound solution and return the JSON check report " +
        "(schemaVersion 3): rules[] keyed by id, plus summary counts. Violations are data — a red rule is a " +
        "finding, not an error. The rules parameter narrows what is evaluated, and the report then covers " +
        "only those. If projects fail to load the report still returns, stamped modelIncomplete: true and " +
        "failedProjects — a verdict reached against a partial model; report that, never plain green. " +
        "Under a .slnf solution filter, uncheckedProjects names the declared projects the run never " +
        "checked — a clean report then covers a subset; say so.";

    private const string StatusDescription =
        "Return the JSON migration burndown (schemaVersion 2): per-rule grandfathered/stale counts and " +
        "promotion suggestions. If projects fail to load the burndown still returns, stamped modelIncomplete: " +
        "true and failedProjects — counts from a partial model; report that rather than quoting them as whole. " +
        "Under a .slnf solution filter, uncheckedProjects names the declared projects the run never checked; " +
        "they contribute no violations, so every count reads low.";

    private const string ExplainDescription =
        "Return one rule's because, fix, posture payload, and linked prose as text.";

    private const string ContextDescription =
        "Return the architecture scope card(s) covering a path — a quarantined scope's dragons + sanctioned surface, " +
        "or a layer's local rules — or a pointer line when none apply. If projects fail to load, the answer opens " +
        "with a caveat naming them: cards from unloaded projects cannot be placed, so treat a no-coverage answer " +
        "as unproven there. A .slnf solution filter gets the same caveat for the declared projects it left " +
        "unchecked: treat a no-coverage answer as unproven under them as well.";

    private const string GraphDescription =
        "Return the JSON codebase survey (schemaVersion 1): projects[] with namespace inventories, " +
        "projectEdges[] (source/target, declared vs observed dependencies), and externalEdges[] grouped by " +
        "namespace root. " +
        "Needs no spec — call it before one exists to plan layers and rules. Needs the solution restored and " +
        "built: if projects fail to load it returns an error naming them rather than a survey missing them. " +
        "Narrow with overview or skeleton (coarser grain) or projects (fewer projects); an over-budget survey " +
        "coarsens its own grain, as far as skeleton, rather than being cut. " +
        "Under a .slnf solution filter, uncheckedProjects names the declared projects the run never loaded — " +
        "a project absent from the survey may simply be out of view.";

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
            "Rule-ID globs, semicolon-separated ('*' spans '/'). Only matching rules run, so rules[] "
            + "and summary cover that subset alone. Matching no rule is an error listing the available IDs.")]
        string? rules = null,
        CancellationToken cancellationToken = default)
    {
        var output = new StringWriter();
        // Exit code and error writer deliberately discarded — everything they would carry is in the document.
        // Violations ride in rules[]; which projects failed to load rides in failedProjects, and the load's
        // own diagnostics ride in workspaceDiagnostics along with the MSBuild-selection note, which
        // WorkspaceDiagnostics puts in the list both surfaces read rather than appending at write time (it
        // was the one line TextWriter.Null used to swallow, and "which MSBuild opened it" is the next
        // question after any load failure). The gate verdict the exit code would have expressed rides in
        // modelIncomplete, and an unmatched --rules filter surfaces as an error result rather than exit 2.
        await new CheckRunner(output, TextWriter.Null, source).RunAsync(
            binding.CheckRequest(diffBase, rules), cancellationToken);
        return output.ToString();
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
        var output = new StringWriter();
        await new StatusRunner(output, TextWriter.Null, source).RunAsync(
            binding.StatusRequest(), cancellationToken);
        return output.ToString();
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
        var output = new StringWriter();
        // Error writer deliberately discarded: explain's answer is spec-derived and cannot be made wrong by
        // a load failure; the caveat channel for a partial model is arch_context's body, not this tool's.
        await new ExplainRunner(output, TextWriter.Null, source).RunAsync(
            binding.ExplainRequest(ruleId), cancellationToken);
        return output.ToString();
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
        [Description("A file or directory path (absolute or solution-relative) to find architecture scope cards for.")]
        string path,
        CancellationToken cancellationToken = default)
    {
        var output = new StringWriter();
        await new ContextRunner(output, source).RunAsync(binding.ContextRequest(path), cancellationToken);
        return output.ToString();
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
            "Elide the namespace inventories and the external-reference rows, keeping every project, its "
            + "declared references and type count, and the observed project edges; the elided rows are "
            + "reported as a count. Coarser than overview, still not a narrower subject.")]
        bool skeleton = false,
        [Description(
            "Project-name globs, semicolon-separated ('*' allowed). References in both directions are "
            + "kept, so an edge can name a project outside the scope. Matching no project is an error listing "
            + "the available names.")]
        string? projects = null,
        CancellationToken cancellationToken = default)
    {
        var output = new StringWriter();
        // Unlike arch_check and arch_status, the incomplete-model verdict cannot ride this document by
        // default — graph refuses before there is one — so the refusal throws and GlobalCallToolFilter
        // returns it as a clean un-logged error result.
        //
        // The response budget is this surface's alone: over it, the runner walks down the grain ladder until a
        // whole document fits instead of handing the truncator one to cut in half. One extraction, and the
        // degraded answer is byte-identical to an explicit call at the grain it landed on, so a reader can
        // trust the grain stamp rather than diffing two surveys. The cap is the truncator's, so degrading
        // fires against exactly the number that would otherwise have truncated.
        await new GraphRunner(output, TextWriter.Null, source).RunAsync(
            binding.GraphRequest(allowWorkspaceDiagnostics, Grain(overview, skeleton), projects, ResponseBudget()),
            cancellationToken);
        return output.ToString();
    }

    // The coarsest flag wins: the two name a floor on detail rather than competing modes, so a caller that
    // passes both is asking for the coarser one, not making an error worth refusing over.
    private static GraphGrain Grain(bool overview, bool skeleton)
    {
        if (skeleton) return GraphGrain.Skeleton;

        return overview ? GraphGrain.Overview : GraphGrain.Full;
    }

    private int ResponseBudget()
    {
        return ResponseTruncator.ComputeMaxChars(environment.GetVariable(LoadBearingEnvVars.MaxMcpOutputTokens));
    }
}
