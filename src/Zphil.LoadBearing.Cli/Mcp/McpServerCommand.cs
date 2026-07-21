using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using Zphil.LoadBearing.Cli.Mcp.Infrastructure;
using Zphil.LoadBearing.Cli.Mcp.Pipeline;
using Zphil.LoadBearing.Cli.Mcp.Prompts;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.MsBuild;

namespace Zphil.LoadBearing.Cli.Mcp;

/// <summary>
///     The <c>loadbearing mcp</c> entry point: a stdio MCP server bound to one solution + spec, exposing
///     the <c>arch_*</c> tools (thin shells over the same runners the CLI commands use).
///     A human who runs it at a terminal gets a hint and exit 2 rather than a hung silent server; a real
///     MCP client over piped stdio gets the file logger, the orphan-server watchdogs, MSBuild
///     registration (JIT-quarantined behind <see cref="EnsureMsBuildRegistered" />), and the host.
/// </summary>
internal static class McpServerCommand
{
    // Set LOADBEARING_DISABLE_WARM_WORKSPACE=true to fall back to the cold one-shot source (following the
    // LOADBEARING_DISABLE_PARENT_WATCH precedent for opting out of an MCP-lifetime behaviour). Read through
    // the IEnvironment seam, never System.Environment, so the env-through-seam ratchet stays green and the
    // test harness can drive both paths without touching real process state.
    internal const string DisableWarmWorkspaceVariable = "LOADBEARING_DISABLE_WARM_WORKSPACE";

    public static async Task<int> RunAsync(McpServerBinding binding, TextWriter error, CancellationToken ct)
    {
        if (!Console.IsInputRedirected)
        {
            // A human ran the tool at a terminal: don't hang on a silent stdio server.
            error.WriteLine("loadbearing mcp is an MCP stdio server; it is started by an MCP client, not interactively.");
            error.WriteLine("Configure it in your MCP client (command \"loadbearing mcp <solution> --spec <spec>\").");
            return 2;
        }

        // Fail fast on a bound solution that cannot be discovered — including a mistyped option that
        // System.CommandLine swallowed as the positional `solution` argument (e.g. `mcp --bogus`). Without
        // this, such a launch would start a server that errors on every tool call, and — because stdin is
        // redirected but never closed under a test host — hang the whole run. The UserErrorException maps to
        // exit 2 at CommandEntryPoint.
        ResolveBoundSolution(binding);

        // A real MCP client launched us over piped stdio. Bring up the file logger and crash handlers
        // before host building so a catastrophic startup failure still lands in the post-mortem log.
        SerilogConfiguration.InitializeFileLogger();
        SerilogConfiguration.RegisterCrashHandlers();

        // Watchers first, so they cover the whole lifetime including MSBuild registration.
        ParentProcessWatcher.Start();
        IdleTimeoutWatchdog.Start();

        EnsureMsBuildRegistered();

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.AddSerilogLogging();

        builder.Services.AddSingleton<IEnvironment, SystemEnvironment>();
        builder.Services.AddSingleton(binding);
        AddWorkspaceSolutionSource(builder.Services);

        builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInstructions = ServerInstructions.Text;
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
    ///     that reuses clean projects' fragments across tool calls, and the
    ///     <see cref="ISolutionSource" /> the tools acquire the solution through. The source is warm by
    ///     default — one session reconciled per call across the server's lifetime, one store paired with it —
    ///     unless <see cref="DisableWarmWorkspaceVariable" /> is <c>true</c>, which resolves the cold one-shot
    ///     <see cref="ColdSolutionSource" /> instead. The warm branch registers the session's disposer with
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

        services.AddSingleton<ISolutionSource>(provider =>
        {
            var environment = provider.GetRequiredService<IEnvironment>();
            if (WarmWorkspaceDisabled(environment)) return new ColdSolutionSource();

            var session = provider.GetRequiredService<WorkspaceSession>();
            var store = provider.GetRequiredService<SessionFragmentStore>();
            ServerShutdown.RegisterDisposer(session.DisposeAsync);
            return new WarmSolutionSource(session, store);
        });
    }

    private static bool WarmWorkspaceDisabled(IEnvironment environment)
    {
        return string.Equals(
            environment.GetVariable(DisableWarmWorkspaceVariable), "true", StringComparison.OrdinalIgnoreCase);
    }

    // Discovers the bound solution (an explicit path, a directory, or a walk-up), throwing a
    // UserErrorException when none resolves. NoInlining keeps it consistent with the quarantine style below;
    // SolutionDiscovery is pure file I/O with no MSBuildWorkspace touch, so it is safe before registration.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ResolveBoundSolution(McpServerBinding binding)
    {
        ModelPipeline.DiscoverSolution(binding.Solution, binding.WorkingDirectory);
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