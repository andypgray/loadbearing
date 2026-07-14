using System.CommandLine;
using Zphil.LoadBearing.Cli.Mcp;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     Builds the command tree (System.CommandLine): the <c>check</c>, <c>explain</c>, <c>render</c>,
///     <c>baseline</c>, <c>status</c>, <c>graph</c>, and <c>mcp</c> commands. Exposed so the in-process
///     e2e tests drive the exact same tree the real entry point does, capturing output via a redirected
///     <see cref="InvocationConfiguration" />.
/// </summary>
internal static class CommandFactory
{
    public static RootCommand BuildRootCommand()
    {
        return new RootCommand("LoadBearing — a fluent architecture spec with deterministic enforcement.")
        {
            BuildCheckCommand(),
            BuildExplainCommand(),
            BuildRenderCommand(),
            BuildBaselineCommand(),
            BuildStatusCommand(),
            BuildGraphCommand(),
            BuildMcpCommand()
        };
    }

    private static Command BuildCheckCommand()
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
                "A git ref; files changed since it are checked against frozen scopes (Freeze tripwire) — warnings only, never failures."
        };

        Command check = new("check", "Evaluate rules against a target solution.")
        {
            solution,
            spec,
            json,
            diffBase
        };

        check.SetAction((parseResult, ct) =>
        {
            var request = new CheckRequest(
                parseResult.GetValue(solution),
                parseResult.GetValue(spec),
                parseResult.GetValue(json),
                parseResult.GetValue(diffBase),
                Directory.GetCurrentDirectory());

            TextWriter output = parseResult.InvocationConfiguration.Output;
            TextWriter error = parseResult.InvocationConfiguration.Error;
            return CommandEntryPoint.RunAsync(() => MsBuildGate.RunCheckAsync(request, output, error, ct), error);
        });

        return check;
    }

    private static Command BuildExplainCommand()
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

        explain.SetAction((parseResult, ct) =>
        {
            var request = new ExplainRequest(
                parseResult.GetValue(ruleId)!,
                parseResult.GetValue(solution),
                parseResult.GetValue(spec),
                Directory.GetCurrentDirectory());

            TextWriter output = parseResult.InvocationConfiguration.Output;
            TextWriter error = parseResult.InvocationConfiguration.Error;
            return CommandEntryPoint.RunAsync(() => MsBuildGate.RunExplainAsync(request, output, ct), error);
        });

        return explain;
    }

    private static Command BuildRenderCommand()
    {
        var solution = SolutionArgument();
        var spec = SpecOption();

        Command render = new("render", "Render the managed AGENTS.md block(s) from the spec.")
        {
            solution,
            spec
        };

        render.SetAction((parseResult, ct) =>
        {
            var request = new RenderRequest(
                parseResult.GetValue(solution),
                parseResult.GetValue(spec),
                Directory.GetCurrentDirectory());

            TextWriter output = parseResult.InvocationConfiguration.Output;
            TextWriter error = parseResult.InvocationConfiguration.Error;
            return CommandEntryPoint.RunAsync(() => MsBuildGate.RunRenderAsync(request, output, error, ct), error);
        });

        return render;
    }

    private static Command BuildBaselineCommand()
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
            Description = "The referenced type of the edge to grandfather — a full type name or 'T:' symbol ID (with --add)."
        };
        Option<string?> subject = new("--subject")
        {
            Description = "The offending type of the shape violation to grandfather — a full type name or 'T:' symbol ID (with --add)."
        };

        Command baseline = new("baseline", "Grandfather, shrink, or add one attributed exception to the ratcheted baselines.")
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
            subject
        };

        baseline.SetAction((parseResult, ct) =>
        {
            var request = new BaselineRequest(
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
                Directory.GetCurrentDirectory());

            TextWriter output = parseResult.InvocationConfiguration.Output;
            TextWriter error = parseResult.InvocationConfiguration.Error;
            return CommandEntryPoint.RunAsync(() => MsBuildGate.RunBaselineAsync(request, output, error, ct), error);
        });

        return baseline;
    }

    private static Command BuildStatusCommand()
    {
        var solution = SolutionArgument();
        var spec = SpecOption();
        Option<bool> json = new("--json")
        {
            Description = "Emit the machine-readable JSON burndown document instead of human-readable output."
        };

        Command status = new("status", "Report per-rule burndown and promotion suggestions; always exits 0.")
        {
            solution,
            spec,
            json
        };

        status.SetAction((parseResult, ct) =>
        {
            var request = new StatusRequest(
                parseResult.GetValue(solution),
                parseResult.GetValue(spec),
                parseResult.GetValue(json),
                Directory.GetCurrentDirectory());

            TextWriter output = parseResult.InvocationConfiguration.Output;
            TextWriter error = parseResult.InvocationConfiguration.Error;
            return CommandEntryPoint.RunAsync(() => MsBuildGate.RunStatusAsync(request, output, error, ct), error);
        });

        return status;
    }

    private static Command BuildGraphCommand()
    {
        var solution = SolutionArgument();
        Option<bool> json = new("--json")
        {
            Description = "Emit the machine-readable JSON survey document instead of human-readable output."
        };

        // Deliberately no --spec: the survey is a property of the codebase, and derive runs before any
        // spec exists (a spec project, once present, appears here as an ordinary project).
        Command graph = new(
            "graph",
            "Summarize the codebase: projects, declared vs observed project references, namespace inventory, and grouped external references. Needs no spec.")
        {
            solution,
            json
        };

        graph.SetAction((parseResult, ct) =>
        {
            var request = new GraphRequest(
                parseResult.GetValue(solution),
                parseResult.GetValue(json),
                Directory.GetCurrentDirectory());

            TextWriter output = parseResult.InvocationConfiguration.Output;
            TextWriter error = parseResult.InvocationConfiguration.Error;
            return CommandEntryPoint.RunAsync(() => MsBuildGate.RunGraphAsync(request, output, error, ct), error);
        });

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

        mcp.SetAction((parseResult, ct) =>
        {
            var binding = new McpServerBinding(
                parseResult.GetValue(solution),
                parseResult.GetValue(spec),
                Directory.GetCurrentDirectory());

            TextWriter error = parseResult.InvocationConfiguration.Error;
            return CommandEntryPoint.RunAsync(() => McpServerCommand.RunAsync(binding, error, ct), error);
        });

        return mcp;
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
}