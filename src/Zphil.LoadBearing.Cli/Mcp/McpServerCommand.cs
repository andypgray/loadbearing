using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using Zphil.LoadBearing.Cli.Mcp.Infrastructure;
using Zphil.LoadBearing.Cli.Mcp.Pipeline;
using Zphil.LoadBearing.Cli.Mcp.Prompts;
using Zphil.LoadBearing.Cli.SpecLoading;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.MsBuild;

namespace Zphil.LoadBearing.Cli.Mcp;

/// <summary>
///     The <c>loadbearing mcp</c> entry point: a stdio MCP server bound to one solution + spec, exposing
///     the <c>arch_*</c> tools (thin shells over the same runners the CLI commands use).
///     A human who runs it at a terminal gets a hint and exit 2 rather than a hung silent server; a real
///     MCP client over piped stdio gets the file logger, the orphan-server watchdogs, MSBuild
///     registration (JIT-quarantined behind <see cref="EnsureMsBuildRegistered" />), and the host.
///     A launch whose optional solution argument was omitted and whose walk-up then found nothing still
///     starts — unbound, announcing the reason in its <c>initialize</c> instructions and returning it from
///     every tool call (<see cref="ResolveBoundSolution" />).
/// </summary>
internal static class McpServerCommand
{
    public static async Task<int> RunAsync(McpServerBinding binding, TextWriter error, CancellationToken ct)
    {
        if (!Console.IsInputRedirected)
        {
            // A human ran the tool at a terminal: don't hang on a silent stdio server.
            error.WriteLine("loadbearing mcp is an MCP stdio server; it is started by an MCP client, not interactively.");
            error.WriteLine("Configure it in your MCP client (command \"loadbearing mcp <solution> --spec <spec>\").");
            return 2;
        }

        // Fail fast on an argument that cannot be discovered; keep the reason and start anyway when there was
        // no argument to be wrong about. See ResolveBoundSolution for why the two halves differ.
        string? bindingFailure = ResolveBoundSolution(binding);

        // The one env seam, built here because the logger and both watchdogs are configured from process
        // state before there is a host to inject it from. Each of them then takes its value as an argument
        // rather than reading at the leaf, so the reads that shape a server's lifetime all happen on this
        // one line of the startup path.
        var environment = new SystemEnvironment();

        // A real MCP client launched us over piped stdio. Bring up the file logger and crash handlers
        // before host building so a catastrophic startup failure still lands in the post-mortem log.
        SerilogConfiguration.InitializeFileLogger(
            environment.GetVariable(LoadBearingEnvVars.LogLevel),
            environment.GetVariable(LoadBearingEnvVars.ClaudeCodeSessionId));
        SerilogConfiguration.RegisterCrashHandlers();

        // Watchers first, so they cover the whole lifetime including MSBuild registration.
        ParentProcessWatcher.Start(IsTrue(environment, LoadBearingEnvVars.DisableParentWatch));
        IdleTimeoutWatchdog.Start(environment.GetVariable(LoadBearingEnvVars.IdleTimeoutMinutes));

        EnsureMsBuildRegistered();

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.AddSerilogLogging();

        builder.Services.AddSingleton<IEnvironment>(environment);
        builder.Services.AddSingleton(binding);
        AddWorkspaceSolutionSource(builder.Services);

        builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInstructions = ServerInstructions.For(bindingFailure);
                options.ServerInfo = new Implementation
                {
                    Name = "loadbearing",
                    Title = "LoadBearing architecture spec",
                    Version = ServerVersion.SemVer
                };
            })
            .WithStdioServerTransport()
            .WithCoercingTools()
            .WithPrompts<ArchPrompts>()
            .WithGlobalCallToolFilter();

        await builder.Build().RunAsync(ct);
        return 0;
    }

    /// <summary>
    ///     Registers the warm-workspace services shared by the production server and the in-process test
    ///     harness, so the two compose the same graph: the long-lived <see cref="WorkspaceSession" /> (its
    ///     reconcile log routed to the host logger), the session-scoped <see cref="SessionFragmentStore" />
    ///     that reuses clean projects' fragments across tool calls, the <see cref="SpecModelCache" /> that
    ///     does the same for the spec's model, and the
    ///     <see cref="ISolutionSource" /> the tools acquire the solution through. The source is warm by
    ///     default — one session reconciled per call across the server's lifetime, one store paired with it —
    ///     unless <see cref="LoadBearingEnvVars.DisableWarmWorkspace" /> is <c>true</c>, which resolves the
    ///     cold one-shot <see cref="ColdSolutionSource" /> instead. Read through the
    ///     <see cref="IEnvironment" /> seam, never <c>System.Environment</c>, so the test harness can drive
    ///     both paths without touching real process state. The warm branch registers the session's disposer with
    ///     <see cref="ServerShutdown" /> lazily, from inside the factory, so a run that never builds a warm
    ///     source wires no disposer.
    /// </summary>
    internal static void AddWorkspaceSolutionSource(IServiceCollection services)
    {
        services.AddSingleton(provider =>
        {
            ILogger logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger<WorkspaceSession>();
            return new WorkspaceSession(message => logger.LogInformation("{ReconcileEvent}", message));
        });

        // One store per server lifetime, paired with the one session. A singleton always, so a test can
        // resolve it; it is only ever populated when the warm source is built and a tool call runs.
        services.AddSingleton(_ => new SessionFragmentStore());

        // The spec side of the same lifetime: one cache of loaded spec models for the server, stamped
        // against the DLLs they came from. Registered unconditionally for the same reason as the store.
        services.AddSingleton(_ => new SpecModelCache());

        services.AddSingleton<ISolutionSource>(provider =>
        {
            var environment = provider.GetRequiredService<IEnvironment>();
            if (IsTrue(environment, LoadBearingEnvVars.DisableWarmWorkspace)) return new ColdSolutionSource();

            var session = provider.GetRequiredService<WorkspaceSession>();
            var store = provider.GetRequiredService<SessionFragmentStore>();
            var specs = provider.GetRequiredService<SpecModelCache>();
            ServerShutdown.RegisterDisposer(session.DisposeAsync);
            return new WarmSolutionSource(session, store, specs);
        });
    }

    // The spelling every LOADBEARING_* opt-out takes: the literal "true", case-insensitively, and nothing
    // else — so a value nobody meant as a flag leaves the behaviour on.
    private static bool IsTrue(IEnvironment environment, string variable)
    {
        return string.Equals(environment.GetVariable(variable), "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Discovers the bound solution (an explicit path, a directory, or a walk-up), returning null on
    ///     success and the refusal text when a walk-up with no argument found nothing.
    /// </summary>
    /// <remarks>
    ///     The two halves differ, and the difference is the point. <b>An argument that does not resolve is fatal</b> —
    ///     including a mistyped option System.CommandLine swallowed as the positional <c>solution</c> (e.g.
    ///     <c>mcp --bogus</c>): the operator named something and it is wrong, so the
    ///     <see cref="UserErrorException" /> propagates to exit 2 at <c>CommandEntryPoint</c>. Starting
    ///     instead would give a server that errors on every tool call and, with stdin redirected but never
    ///     closed under a test host, hang the whole run. <b>No argument is the documented-optional path</b>,
    ///     so its walk-up failing is a condition to report, not a launch to abort: dying here reaches the
    ///     client as "the server failed to start" and no reason at all, since the process is gone before
    ///     <c>initialize</c> can answer. The server starts unbound instead and says why through
    ///     <see cref="ServerInstructions.For" /> and through every tool call, which re-runs the identical
    ///     discovery and gets the identical message.
    ///     Whitespace splits with null because <see cref="ModelPipeline.DiscoverSolution" /> already treats
    ///     the two alike: what makes an argument fatal is discovery actually honouring it.
    ///     NoInlining keeps it consistent with the quarantine style below; SolutionDiscovery is pure file I/O
    ///     with no MSBuildWorkspace touch, so it is safe before registration.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string? ResolveBoundSolution(McpServerBinding binding)
    {
        if (!string.IsNullOrWhiteSpace(binding.Solution))
        {
            ModelPipeline.DiscoverSolution(binding.Solution, binding.WorkingDirectory);
            return null;
        }

        try
        {
            ModelPipeline.DiscoverSolution(null, binding.WorkingDirectory);
            return null;
        }
        catch (UserErrorException ex)
        {
            return ex.Message;
        }
    }

    // JIT quarantine: this NoInlining wrapper is the one sanctioned touch of a Roslyn/MSBuild type in the
    // Mcp path. It registers MSBuildLocator before the host builds; everything else in Mcp\ reaches a
    // Roslyn workspace type only through the runners inside the tool methods (JITted at first call, after
    // registration has run). Mirrors the CLI's MsBuildGate.EnsureMsBuildRegistered.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void EnsureMsBuildRegistered()
    {
        MsBuildBootstrap.EnsureInitialized();
    }
}
