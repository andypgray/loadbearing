using System.CommandLine;
using Zphil.LoadBearing.Cli.Mcp;
using Zphil.LoadBearing.Cli.Mcp.Infrastructure;
using Zphil.LoadBearing.Cli.Rendering;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     Builds the command tree (System.CommandLine): the <c>check</c>, <c>explain</c>, <c>render</c>,
///     <c>baseline</c>, <c>status</c>, <c>graph</c>, and <c>mcp</c> commands. Exposed so the in-process
///     e2e tests drive the exact same tree the real entry point does, capturing output via a redirected
///     <see cref="InvocationConfiguration" />.
/// </summary>
internal static class CommandFactory
{
    /// <param name="hostSource">
    ///     The solution source for any run that would otherwise open a fresh one-shot workspace, or
    ///     <c>null</c> (the real entry point) for <see cref="ColdSolutionSource" />. Carried into every
    ///     workspace command's action; see <see cref="MsBuildGate" /> for what a host source does and does
    ///     not displace.
    /// </param>
    /// <param name="environment">
    ///     The environment seam the cache-root override is read through, or <c>null</c> (the real entry point)
    ///     for real process state. Carried only into the three cache-fronted commands — <c>check</c>,
    ///     <c>status</c>, <c>graph</c> — because they are the only ones that read a variable at all.
    /// </param>
    public static RootCommand BuildRootCommand(
        ISolutionSource? hostSource = null, IEnvironment? environment = null)
    {
        return new RootCommand("LoadBearing — a fluent architecture spec with deterministic enforcement.")
        {
            BuildCheckCommand(hostSource, environment),
            BuildExplainCommand(hostSource),
            BuildRenderCommand(hostSource),
            BuildBaselineCommand(hostSource),
            BuildStatusCommand(hostSource, environment),
            BuildGraphCommand(hostSource, environment),
            BuildMcpCommand()
        };
    }

    private static Command BuildCheckCommand(ISolutionSource? hostSource, IEnvironment? environment)
    {
        var solution = SolutionArgument();
        var spec = SpecOption();
        Option<bool> json = new("--json")
        {
            Description = "Emit the machine-readable JSON document instead of human-readable output."
        };
        Option<string?> diffBase = new("--diff-base")
        {
            Description =
                "A git ref; files changed since it are checked against quarantined scopes (Quarantine tripwire) — warnings only, never failures."
        };
        var allowWorkspaceDiagnostics = AllowWorkspaceDiagnosticsOption(
            "Check against the partial model even when some projects fail to load, instead of failing the run "
            + "with exit 2.");
        Option<string?> sarif = new("--sarif")
        {
            Description =
                "Write a SARIF 2.1.0 report to <path> (GitHub code scanning et al.); human/--json output is unchanged."
        };
        Option<string?> rules = new("--rules")
        {
            Description =
                "Check only the rules whose ID matches these globs (semicolon-separated, '*' allowed and it "
                + "spans '/'); the report, the summary counts and the 0/1 verdict then cover that subset alone. "
                + "A filter matching no rule refuses the run (exit 2) and lists the available rule IDs."
        };
        var noCache = NoCacheOption();
        var binlog = BinlogOption();

        Command check = new(
            "check",
            "Evaluate rules against a target solution; a project that fails to load fails the run (exit 2) unless "
            + "--allow-workspace-diagnostics is passed.")
        {
            solution,
            spec,
            json,
            diffBase,
            allowWorkspaceDiagnostics,
            sarif,
            rules,
            noCache,
            binlog
        };

        SetRequestAction(
            check,
            parseResult => new CheckRequest(
                parseResult.GetValue(solution),
                parseResult.GetValue(spec),
                parseResult.GetValue(json),
                parseResult.GetValue(diffBase),
                Directory.GetCurrentDirectory(),
                parseResult.GetValue(noCache),
                parseResult.GetValue(binlog),
                parseResult.GetValue(allowWorkspaceDiagnostics),
                parseResult.GetValue(sarif),
                parseResult.GetValue(rules)),
            (request, output, error, ct) =>
                MsBuildGate.RunCheckAsync(request, output, error, hostSource, environment, ct));

        return check;
    }

    private static Command BuildExplainCommand(ISolutionSource? hostSource)
    {
        Argument<string> ruleId = new("rule-id")
        {
            Description = "The rule ID to explain (a post-desugar ID, e.g. legacy/billing/containment)."
        };
        var solution = SolutionArgument();
        var spec = SpecOption();

        Command explain = new("explain", "Print a rule's because, fix, posture payload, and linked prose.")
        {
            ruleId,
            solution,
            spec
        };

        SetRequestAction(
            explain,
            parseResult => new ExplainRequest(
                parseResult.GetValue(ruleId)!,
                parseResult.GetValue(solution),
                parseResult.GetValue(spec),
                Directory.GetCurrentDirectory()),
            (request, output, error, ct) => MsBuildGate.RunExplainAsync(request, output, error, hostSource, ct));

        return explain;
    }

    private static Command BuildRenderCommand(ISolutionSource? hostSource)
    {
        var solution = SolutionArgument();
        var spec = SpecOption();
        var allowWorkspaceDiagnostics = AllowWorkspaceDiagnosticsOption(
            "Render from the partial model even when some projects fail to load, instead of refusing the "
            + "command with exit 2.");
        Option<string?> diagram = new("--diagram")
        {
            Description =
                "Also render two Mermaid diagrams into <path>'s managed block: the codebase graph "
                + "(projects and their cross-project references; solid = observed, dotted = declared but unobserved), "
                + "then the spec's law (the places it names, what they must not reference, and the debt it grandfathers)."
        };
        Option<string?> diagramOnly = new("--diagram-only")
        {
            Description =
                "Draw only the projects matching these name globs (semicolon-separated, '*' allowed); "
                + "scopes the codebase survey fence only, never the law fence. With --diagram."
        };
        Option<string?> diagramExclude = new("--diagram-exclude")
        {
            Description =
                "Drop the projects matching these name globs (semicolon-separated, '*' allowed); "
                + "scopes the codebase survey fence only, never the law fence. With --diagram."
        };

        Command render = new(
            "render",
            "Render the managed AGENTS.md block(s) from the spec; a project that fails to load refuses the "
            + "command (exit 2) unless --allow-workspace-diagnostics is passed.")
        {
            solution,
            spec,
            allowWorkspaceDiagnostics,
            diagram,
            diagramOnly,
            diagramExclude
        };

        SetRequestAction(
            render,
            parseResult => new RenderRequest(
                parseResult.GetValue(solution),
                parseResult.GetValue(spec),
                Directory.GetCurrentDirectory(),
                parseResult.GetValue(allowWorkspaceDiagnostics),
                parseResult.GetValue(diagram),
                parseResult.GetValue(diagramOnly),
                parseResult.GetValue(diagramExclude)),
            (request, output, error, ct) => MsBuildGate.RunRenderAsync(request, output, error, hostSource, ct));

        return render;
    }

    private static Command BuildBaselineCommand(ISolutionSource? hostSource)
    {
        var solution = SolutionArgument();
        var spec = SpecOption();
        Option<bool> init = new("--init")
        {
            Description = "Grandfather each ratcheted rule's current violations into its baseline (uncaptured rules only)."
        };
        Option<bool> acceptReductions = new("--accept-reductions")
        {
            Description = "Remove baseline entries whose violations no longer occur; never adds."
        };
        Option<bool> add = new("--add")
        {
            Description = "Grandfather exactly one currently observed violation of a captured rule, with attribution."
        };
        Option<string?> rule = new("--rule")
        {
            Description = "The ratcheted rule ID to add the entry to (with --add)."
        };
        Option<string?> because = new("--because")
        {
            Description = "Why this entry is grandfathered — recorded in the baseline entry (single line; with --add)."
        };
        Option<string?> source = new("--source")
        {
            Description = "The referencing type of the edge to grandfather — a full type name or 'T:' symbol ID (with --add)."
        };
        Option<string?> target = new("--target")
        {
            Description =
                "The referenced type of the edge to grandfather — a full type name or 'T:' symbol ID, or a banned "
                + "member's full name (System.DateTime.Now) or member symbol ID (P:System.DateTime.Now) (with --add)."
        };
        Option<string?> subject = new("--subject")
        {
            Description = "The offending type of the shape violation to grandfather — a full type name or 'T:' symbol ID (with --add)."
        };
        var allowWorkspaceDiagnostics = AllowWorkspaceDiagnosticsOption(
            "Write baselines from the partial model even when some projects fail to load, instead of refusing "
            + "the command with exit 2.");

        Command baseline = new(
            "baseline",
            "Grandfather, shrink, or add one attributed exception to the ratcheted baselines; a project that fails "
            + "to load refuses the command (exit 2) unless --allow-workspace-diagnostics is passed.")
        {
            solution,
            spec,
            init,
            acceptReductions,
            add,
            rule,
            because,
            source,
            target,
            subject,
            allowWorkspaceDiagnostics
        };

        SetRequestAction(
            baseline,
            parseResult => new BaselineRequest(
                parseResult.GetValue(solution),
                parseResult.GetValue(spec),
                parseResult.GetValue(init),
                parseResult.GetValue(acceptReductions),
                parseResult.GetValue(add),
                parseResult.GetValue(rule),
                parseResult.GetValue(because),
                parseResult.GetValue(source),
                parseResult.GetValue(target),
                parseResult.GetValue(subject),
                Directory.GetCurrentDirectory(),
                parseResult.GetValue(allowWorkspaceDiagnostics)),
            (request, output, error, ct) => MsBuildGate.RunBaselineAsync(request, output, error, hostSource, ct));

        return baseline;
    }

    private static Command BuildStatusCommand(ISolutionSource? hostSource, IEnvironment? environment)
    {
        var solution = SolutionArgument();
        var spec = SpecOption();
        Option<bool> json = new("--json")
        {
            Description = "Emit the machine-readable JSON burndown document instead of human-readable output."
        };
        var allowWorkspaceDiagnostics = AllowWorkspaceDiagnosticsOption(
            "Report the burndown from the partial model even when some projects fail to load, instead of "
            + "exiting 2 after rendering it.");
        var noCache = NoCacheOption();
        var binlog = BinlogOption();

        Command status = new(
            "status",
            "Report per-rule burndown and promotion suggestions; red rules never fail the run, but a project that "
            + "fails to load does (exit 2) unless --allow-workspace-diagnostics is passed.")
        {
            solution,
            spec,
            json,
            allowWorkspaceDiagnostics,
            noCache,
            binlog
        };

        SetRequestAction(
            status,
            parseResult => new StatusRequest(
                parseResult.GetValue(solution),
                parseResult.GetValue(spec),
                parseResult.GetValue(json),
                Directory.GetCurrentDirectory(),
                parseResult.GetValue(noCache),
                parseResult.GetValue(binlog),
                parseResult.GetValue(allowWorkspaceDiagnostics)),
            (request, output, error, ct) =>
                MsBuildGate.RunStatusAsync(request, output, error, hostSource, environment, ct));

        return status;
    }

    private static Command BuildGraphCommand(ISolutionSource? hostSource, IEnvironment? environment)
    {
        var solution = SolutionArgument();
        Option<bool> json = new("--json")
        {
            Description = "Emit the machine-readable JSON survey document instead of human-readable output."
        };
        var allowWorkspaceDiagnostics = AllowWorkspaceDiagnosticsOption(
            "Survey the partial model even when some projects fail to load, instead of refusing with exit 2.");
        var noCache = NoCacheOption();
        var binlog = BinlogOption();
        Option<bool> overview = new("--overview")
        {
            Description =
                "Summarize at overview grain: the whole survey with each project's namespace inventory elided "
                + "— coarser, not narrower."
        };
        Option<bool> skeleton = new("--skeleton")
        {
            Description =
                "Summarize at skeleton grain: the structural spine only — every project with its declared "
                + "references and type count, and the observed project edges. Namespace inventories and "
                + "external references are both elided, the latter reported as a count. Coarser than "
                + "--overview, and still not narrower."
        };
        Option<string?> projects = new("--projects")
        {
            Description =
                "Survey only the projects matching these name globs (semicolon-separated, '*' allowed); "
                + "references in both directions are kept, so an edge can name a project outside the scope. "
                + "A filter matching no project refuses the survey (exit 2) and lists the available names."
        };

        // Deliberately no --spec: the survey is a property of the codebase, and derive runs before any
        // spec exists (a spec project, once present, appears here as an ordinary project).
        Command graph = new(
            "graph",
            "Summarize the codebase: projects, declared vs observed project references, namespace inventory, and "
            + "grouped external references. Needs no spec; a project that fails to load refuses the survey (exit 2) "
            + "unless --allow-workspace-diagnostics is passed.")
        {
            solution,
            json,
            allowWorkspaceDiagnostics,
            noCache,
            binlog,
            overview,
            skeleton,
            projects
        };

        SetRequestAction(
            graph,
            parseResult => new GraphRequest(
                parseResult.GetValue(solution),
                parseResult.GetValue(json),
                Directory.GetCurrentDirectory(),
                parseResult.GetValue(noCache),
                parseResult.GetValue(binlog),
                parseResult.GetValue(allowWorkspaceDiagnostics),
                // The coarsest flag wins: the two name a floor on detail, so asking for both is not a
                // conflict to refuse over.
                parseResult.GetValue(skeleton) ? GraphGrain.Skeleton
                : parseResult.GetValue(overview) ? GraphGrain.Overview
                : GraphGrain.Full,
                parseResult.GetValue(projects),
                // The auto-degrade budget is a property of the caller's transport, and a terminal has none:
                // named here so its absence reads as a decision rather than an omission.
                // ReSharper disable once ArgumentsStyleNamedExpression
                ResponseBudgetChars: null),
            (request, output, error, ct) =>
                MsBuildGate.RunGraphAsync(request, output, error, hostSource, environment, ct));

        return graph;
    }

    private static Command BuildMcpCommand()
    {
        var solution = SolutionArgument();
        var spec = SpecOption();

        Command mcp = new("mcp", "Run the MCP stdio server bound to a solution and spec.")
        {
            solution,
            spec
        };

        // The one command with nothing to write to stdout: the server owns it as a JSON-RPC channel, so the
        // output writer is discarded here rather than handed on.
        SetRequestAction(
            mcp,
            parseResult => new McpServerBinding(
                parseResult.GetValue(solution),
                parseResult.GetValue(spec),
                Directory.GetCurrentDirectory()),
            (binding, _, error, ct) => McpServerCommand.RunAsync(binding, error, ct));

        return mcp;
    }

    // The action shape every command shares: build the request off the parse, then run it under the
    // top-level error handler with the SAME error writer the run itself writes to — one place, so a verb
    // added later cannot wire the two apart, and CliErrorMapper cannot end up writing somewhere the verb's
    // own diagnostics did not.
    private static void SetRequestAction<TRequest>(
        Command command,
        Func<ParseResult, TRequest> requestFrom,
        Func<TRequest, TextWriter, TextWriter, CancellationToken, Task<int>> run)
    {
        command.SetAction((parseResult, ct) =>
        {
            TRequest request = requestFrom(parseResult);

            TextWriter output = parseResult.InvocationConfiguration.Output;
            TextWriter error = parseResult.InvocationConfiguration.Error;
            return CommandEntryPoint.RunAsync(() => run(request, output, error, ct), error);
        });
    }

    private static Argument<string?> SolutionArgument()
    {
        return new Argument<string?>("solution")
        {
            Description = "Solution file, a directory to search, or omitted to walk up from the working directory.",
            Arity = ArgumentArity.ZeroOrOne
        };
    }

    private static Option<string?> SpecOption()
    {
        return new Option<string?>("--spec")
        {
            Description = "A built spec DLL, or a csproj that is a member of the target solution. Omit to use the convention."
        };
    }

    private static Option<bool> NoCacheOption()
    {
        return new Option<bool>("--no-cache")
        {
            Description =
                "Bypass the persisted caches (extraction fragments and the build capture): always load and extract "
                + "fresh, and write nothing back."
        };
    }

    // The one opt-out out of the incomplete-model gate, worded per verb but always the same flag name: the
    // five verbs that consume the model answer a partial load the same way, so an operator learns one flag.
    private static Option<bool> AllowWorkspaceDiagnosticsOption(string description)
    {
        return new Option<bool>("--allow-workspace-diagnostics") { Description = description };
    }

    private static Option<string?> BinlogOption()
    {
        return new Option<string?>("--binlog")
        {
            Description =
                "A .binlog from a real build of this solution on this machine. Replays the captured structure "
                + "instead of a design-time build; the capture persists, and later runs replay it automatically "
                + "while structurally valid."
        };
    }
}
