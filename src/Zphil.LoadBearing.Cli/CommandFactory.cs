using System.CommandLine;
using Zphil.LoadBearing.Cli.Mcp;
using Zphil.LoadBearing.Cli.Mcp.Infrastructure;
using Zphil.LoadBearing.Cli.Pipeline;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Cli.Verbs;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     Builds the command tree (System.CommandLine): every verb the CLI exposes, each with its options and
///     its action. Exposed so the in-process e2e tests drive the exact same tree the real entry point does,
///     capturing output via a redirected <see cref="InvocationConfiguration" />.
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
    ///     for real process state. Carried into every command that consumes a model, since all six front the
    ///     persisted extraction cache, and so all six resolve a cache root.
    /// </param>
    public static RootCommand BuildRootCommand(
        ISolutionSource? hostSource = null, IEnvironment? environment = null)
    {
        return new RootCommand("LoadBearing — a fluent architecture spec with deterministic enforcement.")
        {
            BuildCheckCommand(hostSource, environment),
            BuildExplainCommand(hostSource, environment),
            BuildRenderCommand(hostSource, environment),
            BuildBaselineCommand(hostSource, environment),
            BuildStatusCommand(hostSource, environment),
            BuildGraphCommand(hostSource, environment),
            BuildMcpCommand()
        };
    }

    private static Command BuildCheckCommand(ISolutionSource? hostSource, IEnvironment? environment)
    {
        Argument<string?> solution = SolutionArgument();
        Option<string?> spec = SpecOption();
        Option<bool> json = new("--json")
        {
            Description = "Emit the machine-readable JSON document instead of human-readable output."
        };
        Option<bool> hookJson = new("--hook-json")
        {
            Description =
                "Render for a Claude Code PostToolUse hook: a clean run carrying warnings writes the report "
                + "as hookSpecificOutput.additionalContext and nothing else, so the hook's stdout reaches the "
                + "agent; a clean run with no warnings writes nothing; violations print as usual. Not with --json."
        };
        Option<string?> diffBase = new("--diff-base")
        {
            Description =
                "A git ref; files changed since it are checked against quarantined and cautioned scopes (the scope tripwire) — warnings only, never failures."
        };
        Option<bool> allowWorkspaceDiagnostics = AllowWorkspaceDiagnosticsOption(
            "Check against the partial model even when some projects fail to load or to restore, instead of "
            + "failing the run with exit 2.");
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
        Option<bool> overview = new("--overview")
        {
            Description =
                "Summarize the --json report at overview grain: every rule and every violation, with each "
                + "violation's sites replaced by their count — coarser, not narrower."
        };
        Option<bool> skeleton = new("--skeleton")
        {
            Description =
                "Summarize the --json report at skeleton grain: the verdict alone — every rule with its "
                + "prose, status, baseline and warnings, its violations replaced by their count. Coarser "
                + "than --overview, and still not narrower."
        };
        Option<bool> index = new("--index")
        {
            Description =
                "Summarize the --json report at index grain: a verdict per rule ID — posture, status, "
                + "baseline, warnings and violation count — with the rule's prose and the workspace "
                + "diagnostics elided (workspaceDiagnosticCount says how many) and every trust stamp kept. "
                + "The coarsest grain, and the menu --rules globs pick from; still not narrower."
        };
        Option<bool> noCache = NoCacheOption();
        Option<string?> binlog = BinlogOption();

        Command check = new(
            "check",
            "Evaluate rules against a target solution; a project that fails to load, or whose NuGet packages did "
            + "not resolve, fails the run (exit 2) unless --allow-workspace-diagnostics is passed.")
        {
            solution,
            spec,
            json,
            hookJson,
            diffBase,
            allowWorkspaceDiagnostics,
            sarif,
            rules,
            overview,
            skeleton,
            index,
            noCache,
            binlog
        };

        SetRequestAction(
            check,
            parseResult => new CheckRequest(
                parseResult.GetValue(solution),
                parseResult.GetValue(spec),
                parseResult.GetValue(json),
                parseResult.GetValue(hookJson),
                parseResult.GetValue(diffBase),
                Directory.GetCurrentDirectory(),
                parseResult.GetValue(noCache),
                parseResult.GetValue(binlog),
                parseResult.GetValue(allowWorkspaceDiagnostics),
                parseResult.GetValue(sarif),
                parseResult.GetValue(rules),
                DocumentGrains.Coarsest(
                    parseResult.GetValue(overview), parseResult.GetValue(skeleton), parseResult.GetValue(index))),
            (request, output, error, ct) =>
                MsBuildGate.RunCheckAsync(request, output, error, hostSource, environment, ct));

        return check;
    }

    private static Command BuildExplainCommand(ISolutionSource? hostSource, IEnvironment? environment)
    {
        Argument<string> ruleId = new("rule-id")
        {
            Description = "The rule ID to explain (a post-desugar ID, e.g. legacy/billing/containment)."
        };
        Argument<string?> solution = SolutionArgument();
        Option<string?> spec = SpecOption();
        Option<bool> noCache = NoCacheOption();

        Command explain = new("explain", "Print a rule's because, fix, posture payload, and linked prose.")
        {
            ruleId,
            solution,
            spec,
            noCache
        };

        SetRequestAction(
            explain,
            parseResult => new ExplainRequest(
                parseResult.GetValue(ruleId)!,
                parseResult.GetValue(solution),
                parseResult.GetValue(spec),
                Directory.GetCurrentDirectory(),
                parseResult.GetValue(noCache)),
            (request, output, error, ct) =>
                MsBuildGate.RunExplainAsync(request, output, error, hostSource, environment, ct));

        return explain;
    }

    private static Command BuildRenderCommand(ISolutionSource? hostSource, IEnvironment? environment)
    {
        Argument<string?> solution = SolutionArgument();
        Option<string?> spec = SpecOption();
        Option<bool> allowWorkspaceDiagnostics = AllowWorkspaceDiagnosticsOption(
            "Render from the partial model even when some projects fail to load or to restore, instead of refusing "
            + "the command with exit 2.");
        Option<string?> diagram = new("--diagram")
        {
            Description =
                "Also render two Mermaid diagrams into <path>'s managed block: the codebase graph "
                + "(projects and their cross-project references; solid = observed, dotted = declared but unobserved), "
                + "then the spec's law (the places it names, what they must not reference, and the debt it grandfathers). "
                + "The graph draws the projects the solution file declares, so a project a ProjectReference "
                + "dragged into the build is not drawn; `graph` lists every loaded project and says which is which."
        };
        Option<string?> diagramOnly = new("--diagram-only")
        {
            Description =
                "Draw only the declared projects matching these name globs (semicolon-separated, '*' allowed) — "
                + "for legibility on a large solution, not to keep a foreign project out. "
                + "Scopes the codebase survey fence only, never the law fence. With --diagram."
        };
        Option<string?> diagramExclude = new("--diagram-exclude")
        {
            Description =
                "Drop the declared projects matching these name globs (semicolon-separated, '*' allowed) — "
                + "for legibility on a large solution, not to keep a foreign project out. "
                + "Scopes the codebase survey fence only, never the law fence. With --diagram."
        };
        Option<bool> noCache = NoCacheOption();

        Command render = new(
            "render",
            "Render the managed AGENTS.md block(s) from the spec; a project that fails to load, or whose NuGet "
            + "packages did not resolve, refuses the command (exit 2) unless --allow-workspace-diagnostics is "
            + "passed.")
        {
            solution,
            spec,
            allowWorkspaceDiagnostics,
            diagram,
            diagramOnly,
            diagramExclude,
            noCache
        };

        SetRequestAction(
            render,
            parseResult => new RenderRequest(
                parseResult.GetValue(solution),
                parseResult.GetValue(spec),
                Directory.GetCurrentDirectory(),
                parseResult.GetValue(noCache),
                parseResult.GetValue(allowWorkspaceDiagnostics),
                parseResult.GetValue(diagram),
                parseResult.GetValue(diagramOnly),
                parseResult.GetValue(diagramExclude)),
            (request, output, error, ct) =>
                MsBuildGate.RunRenderAsync(request, output, error, hostSource, environment, ct));

        return render;
    }

    private static Command BuildBaselineCommand(ISolutionSource? hostSource, IEnvironment? environment)
    {
        Argument<string?> solution = SolutionArgument();
        Option<string?> spec = SpecOption();
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
        Option<bool> allowWorkspaceDiagnostics = AllowWorkspaceDiagnosticsOption(
            "Write baselines from the partial model even when some projects fail to load or to restore, instead of "
            + "refusing the command with exit 2.");
        // Offered on the whole verb but load-bearing for --add alone: --init and --accept-reductions extract
        // cache-free whatever it says, because both read absence as evidence. One flag name
        // across every verb is what an operator learns; a mode where it changes nothing is not worth a second.
        Option<bool> noCache = NoCacheOption();

        Command baseline = new(
            "baseline",
            "Grandfather, shrink, or add one attributed exception to the ratcheted baselines; a project that fails "
            + "to load, or whose NuGet packages did not resolve, refuses the command (exit 2) unless "
            + "--allow-workspace-diagnostics is passed.")
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
            allowWorkspaceDiagnostics,
            noCache
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
                parseResult.GetValue(noCache),
                parseResult.GetValue(allowWorkspaceDiagnostics)),
            (request, output, error, ct) =>
                MsBuildGate.RunBaselineAsync(request, output, error, hostSource, environment, ct));

        return baseline;
    }

    private static Command BuildStatusCommand(ISolutionSource? hostSource, IEnvironment? environment)
    {
        Argument<string?> solution = SolutionArgument();
        Option<string?> spec = SpecOption();
        Option<bool> json = new("--json")
        {
            Description = "Emit the machine-readable JSON burndown document instead of human-readable output."
        };
        Option<bool> allowWorkspaceDiagnostics = AllowWorkspaceDiagnosticsOption(
            "Report the burndown from the partial model even when some projects fail to load or to restore, "
            + "instead of exiting 2 after rendering it.");
        Option<bool> noCache = NoCacheOption();
        Option<string?> binlog = BinlogOption();

        Command status = new(
            "status",
            "Report per-rule burndown and promotion suggestions; red rules never fail the run, but a project that "
            + "fails to load or to restore does (exit 2) unless --allow-workspace-diagnostics is passed.")
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
        Argument<string?> solution = SolutionArgument();
        Option<bool> json = new("--json")
        {
            Description = "Emit the machine-readable JSON survey document instead of human-readable output."
        };
        Option<bool> allowWorkspaceDiagnostics = AllowWorkspaceDiagnosticsOption(
            "Survey the partial model even when some projects fail to load or to restore, instead of refusing with "
            + "exit 2.");
        Option<bool> noCache = NoCacheOption();
        Option<string?> binlog = BinlogOption();
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
        Option<bool> index = new("--index")
        {
            Description =
                "Summarize at index grain: the project roster only — every project with its solution "
                + "membership and type count, its declared references and target frameworks elided, and the "
                + "observed edges and workspace diagnostics each reported as a count. The coarsest grain, "
                + "and the menu --projects globs pick from; still not narrower."
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
            + "grouped external references. Needs no spec; a project that fails to load or to restore refuses the "
            + "survey (exit 2) unless --allow-workspace-diagnostics is passed.")
        {
            solution,
            json,
            allowWorkspaceDiagnostics,
            noCache,
            binlog,
            overview,
            skeleton,
            index,
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
                DocumentGrains.Coarsest(
                    parseResult.GetValue(overview), parseResult.GetValue(skeleton), parseResult.GetValue(index)),
                parseResult.GetValue(projects)),
            (request, output, error, ct) =>
                MsBuildGate.RunGraphAsync(request, output, error, hostSource, environment, ct));

        return graph;
    }

    private static Command BuildMcpCommand()
    {
        Argument<string?> solution = SolutionArgument();
        Option<string?> spec = SpecOption();

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
