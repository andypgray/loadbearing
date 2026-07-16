using System.IO.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Zphil.LoadBearing.Cli.Mcp;
using Zphil.LoadBearing.Cli.Mcp.Infrastructure;
using Zphil.LoadBearing.Cli.Mcp.Pipeline;
using Zphil.LoadBearing.Cli.Mcp.Prompts;
using Zphil.LoadBearing.Tests.Mcp.TestDoubles;

namespace Zphil.LoadBearing.Tests.Mcp;

/// <summary>
///     A real MCP client and server connected in-process over a pair of pipes — no child process, no real
///     stdio — composed exactly as <c>McpServerCommand</c> composes the production server (same DI graph,
///     including the shared warm <c>WorkspaceSession</c> + <c>ISolutionSource</c> registration, with the
///     <see cref="IEnvironment" /> seam faked, the coercing tools, the prompt surface, and global
///     call-tool filter installed), swapping stdio for a stream transport. The one deliberate omission: the harness never
///     starts the orphan-server watchdogs, so <c>Environment.Exit</c> can never fire during a test.
///     Per-test-instance and lets integration tests drive the <c>tools/call</c> pipeline end to end and
///     assert both on what the client sees and on what the server logged. Disposal tears the host down
///     asynchronously (the warm session is <see cref="IAsyncDisposable" />) and resets
///     <c>ServerShutdown</c>'s registered disposers so the warm session's disposer cannot leak across tests.
/// </summary>
internal sealed class McpPipelineHarness : IAsyncDisposable
{
    private readonly IHost _host;

    private McpPipelineHarness(IHost host, McpClient client, FakeEnvironment environment, CapturingLoggerProvider logs)
    {
        _host = host;
        Client = client;
        Environment = environment;
        Logs = logs;
    }

    /// <summary>The connected client, past the <c>initialize</c> handshake — call <c>ListTools</c>/<c>CallTool</c> on it.</summary>
    public McpClient Client { get; }

    /// <summary>The server's environment seam: set <c>MAX_MCP_OUTPUT_TOKENS</c>, etc.</summary>
    public FakeEnvironment Environment { get; }

    /// <summary>Everything the server logged through its <see cref="ILoggerFactory" /> during the session.</summary>
    public CapturingLoggerProvider Logs { get; }

    /// <summary>
    ///     The server's root service provider, so tests can resolve the composed <c>ISolutionSource</c> (to
    ///     assert cold vs warm selection) or the singleton <c>WorkspaceSession</c> (to read its reconcile
    ///     counters after driving tool calls).
    /// </summary>
    public IServiceProvider Services => _host.Services;

    public async ValueTask DisposeAsync()
    {
        // Dispose order matters: closing the client sends EOF, which ends the single-session server's
        // RunAsync and triggers host shutdown; then stop the host (bounded so a stuck stop can't hang the
        // suite).
        await Client.DisposeAsync();

        using CancellationTokenSource stopTimeout = new(TimeSpan.FromSeconds(30));
        await _host.StopAsync(stopTimeout.Token);

        // DisposeAsync, not Dispose: the warm WorkspaceSession singleton is IAsyncDisposable-only, and a
        // synchronous container dispose throws when it meets one. This also awaits the session's own
        // MSBuildWorkspace/BuildHost teardown before the next serial test opens its workspace.
        if (_host is IAsyncDisposable asyncHost)
            await asyncHost.DisposeAsync();
        else
            _host.Dispose();

        // The warm ISolutionSource factory registers the session's disposer on the static ServerShutdown
        // list; clear it so registrations don't accumulate (or dangle onto a disposed session) across the
        // serial suite — the same reset idiom ServerShutdownTests uses.
        ServerShutdown.ResetForTests();
    }

    /// <summary>
    ///     Builds the host bound to <paramref name="binding" />, starts it, and connects a client over the
    ///     pipe pair. Mirrors <c>McpServerCommand</c>'s <c>AddMcpServer</c> + <c>WithCoercingTools</c> +
    ///     <c>WithPrompts</c> + <c>WithGlobalCallToolFilter</c> composition, swapping the stdio transport for
    ///     a stream transport over in-memory pipes.
    /// </summary>
    public static async Task<McpPipelineHarness> StartAsync(McpServerBinding binding, CancellationToken cancellationToken)
    {
        FakeEnvironment environment = new();
        CapturingLoggerProvider logs = new();

        // Two unidirectional pipes: client -> server and server -> client. Created before
        // WithStreamServerTransport, which constructs the server transport eagerly at registration.
        Pipe clientToServer = new();
        Pipe serverToClient = new();

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();

        // Capture everything the server logs and nothing else, so "no warning" / "exactly one warning"
        // assertions see the filter alone rather than the default console/debug providers.
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);

        // The McpServerCommand service graph, with the env seam faked and the binding under test. The warm
        // WorkspaceSession + ISolutionSource factory is registered through the very method the production
        // server calls, so a flip of the LOADBEARING_DISABLE_WARM_WORKSPACE flag on this harness's fake
        // environment exercises the real cold/warm selection.
        builder.Services.AddSingleton<IEnvironment>(environment);
        builder.Services.AddSingleton(binding);
        McpServerCommand.AddWorkspaceSolutionSource(builder.Services);

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
            .WithCoercingTools()
            .WithPrompts<ArchPrompts>()
            .WithGlobalCallToolFilter()
            .WithStreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream());

        IHost host = builder.Build();
        await host.StartAsync(cancellationToken);

        StreamClientTransport clientTransport = new(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream());
        var client = await McpClient.CreateAsync(clientTransport, cancellationToken: cancellationToken);

        return new McpPipelineHarness(host, client, environment, logs);
    }
}