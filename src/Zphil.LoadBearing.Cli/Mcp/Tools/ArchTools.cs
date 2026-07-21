using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Zphil.LoadBearing.Cli.Mcp.Tools;

/// <summary>
///     The MCP tool surface: the five <c>arch_*</c> tools, each a thin shell that runs the
///     same internal runner its CLI verb uses against the bound solution + spec, captures stdout into a
///     <see cref="StringWriter" />, and returns the text — so CLI and MCP output are identical by
///     construction (pinned by <c>CliMcpParityTests</c>). Violations are data, never tool errors. Tool
///     methods never <c>try/catch</c>: they throw, and <see cref="Pipeline.GlobalCallToolFilter" /> shapes
///     any <see cref="Roslyn.UserErrorException" /> or spec-validation failure into an error result.
///     Reaching a Roslyn workspace type only through the runners keeps the MSBuildLocator JIT quarantine
///     intact — these methods are first JITted at the first tool call, after registration has run. Every
///     runner is handed the injected <see cref="ISolutionSource" /> so tool calls acquire the solution the
///     same way: warm (a session reconciled across calls) by default, or cold when the warm workspace is
///     disabled — the CLI's own default source.
/// </summary>
[McpServerToolType]
internal sealed class ArchTools(McpServerBinding binding, ISolutionSource source)
{
    internal const string CheckToolName = "arch_check";
    internal const string StatusToolName = "arch_status";
    internal const string ExplainToolName = "arch_explain";
    internal const string ContextToolName = "arch_context";
    internal const string GraphToolName = "arch_graph";

    private const string CheckDescription =
        "Run the whole architecture spec against the bound solution and return the JSON check report " +
        "(schemaVersion 3). Violations are data — read summary and rules[]; a red rule is a finding, not an error.";

    private const string StatusDescription =
        "Return the JSON burndown (schemaVersion 2): per-rule grandfathered/stale counts and promotion suggestions.";

    private const string ExplainDescription =
        "Return one rule's because, fix, posture payload, and linked prose as text.";

    private const string ContextDescription =
        "Return the architecture scope card(s) covering a path — a frozen scope's dragons + sanctioned surface, " +
        "or a layer's local rules — or a pointer line when none apply.";

    private const string GraphDescription =
        "Return the JSON codebase survey (schemaVersion 1): projects with namespace inventories, declared vs " +
        "observed project references, and external references grouped by namespace root. Needs no spec — call " +
        "it before one exists to plan layers and rules.";

    [McpServerTool(
        Name = CheckToolName,
        Title = "Architecture Check",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description(CheckDescription)]
    public async Task<string> CheckAsync(
        [Description("Optional git ref; files changed since it that fall in a frozen scope raise a tripwire warning.")]
        string? diffBase = null,
        CancellationToken cancellationToken = default)
    {
        var output = new StringWriter();
        // Exit code deliberately ignored — violations ride in the JSON; stderr diagnostics ride in the
        // document's workspaceDiagnostics, so the error writer is discarded. NoCache: the warm workspace and
        // the persisted cache keep independent lifetimes — a tool call never reads or writes the cache file.
        // Binlog null: the warm path never uses the build capture (latency-critical callers ride the session).
        // AllowWorkspaceDiagnostics false: the fail-closed gate would fire on a load failure, but it writes only to the
        // discarded error writer and returns an ignored exit code — it cannot touch the JSON this tool returns.
        await new CheckRunner(output, TextWriter.Null, source).RunAsync(
            new CheckRequest(binding.Solution, binding.Spec, true, diffBase, binding.WorkingDirectory, true, null, false, null),
            cancellationToken);
        return output.ToString();
    }

    [McpServerTool(
        Name = StatusToolName,
        Title = "Architecture Status",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description(StatusDescription)]
    public async Task<string> StatusAsync(CancellationToken cancellationToken = default)
    {
        var output = new StringWriter();
        // Binlog null: the warm path never uses the build capture.
        await new StatusRunner(output, TextWriter.Null, source).RunAsync(
            new StatusRequest(binding.Solution, binding.Spec, true, binding.WorkingDirectory, true, null),
            cancellationToken);
        return output.ToString();
    }

    [McpServerTool(
        Name = ExplainToolName,
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
        await new ExplainRunner(output, source).RunAsync(
            new ExplainRequest(ruleId, binding.Solution, binding.Spec, binding.WorkingDirectory), cancellationToken);
        return output.ToString();
    }

    [McpServerTool(
        Name = ContextToolName,
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
        await new ContextRunner(output, source).RunAsync(
            new ContextRequest(path, binding.Solution, binding.Spec, binding.WorkingDirectory), cancellationToken);
        return output.ToString();
    }

    [McpServerTool(
        Name = GraphToolName,
        Title = "Architecture Graph",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description(GraphDescription)]
    public async Task<string> GraphAsync(CancellationToken cancellationToken = default)
    {
        var output = new StringWriter();
        // binding.Spec is deliberately unused: the survey is a property of the codebase, and derive runs
        // before any spec exists (a spec project would appear here as an ordinary project). Binlog null: the
        // warm path never uses the build capture.
        await new GraphRunner(output, TextWriter.Null, source).RunAsync(
            new GraphRequest(binding.Solution, true, binding.WorkingDirectory, true, null), cancellationToken);
        return output.ToString();
    }
}