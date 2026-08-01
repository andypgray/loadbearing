using System.Runtime.CompilerServices;
using Microsoft.Build.Locator;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.MsBuild;

namespace Zphil.LoadBearing.ArchSpec;

/// <summary>
///     LoadBearing's own architecture spec — the dogfood render source, exercising the full posture and
///     verb range on this repo's real code so the product governs itself honestly. Every rule below is a
///     genuine boundary: nothing in the build system prevents breaking it. The rendered block lives in the
///     committed root <c>AGENTS.md</c>, kept current by the self-spec tests.
///     <list type="bullet">
///         <item>
///             <b>Enforce</b> — the always-true laws.
///             <list type="bullet">
///                 <item>
///                     <c>layering/core-no-roslyn</c>: Core, the netstandard2.0 reified model both render
///                     targets consume, references neither the Roslyn extraction project nor the
///                     <c>Microsoft.CodeAnalysis</c>/<c>Microsoft.Build</c> packages behind it (a package
///                     reference is the route the build cannot block).
///                 </item>
///                 <item>
///                     <c>cli/no-stdout</c>: the CLI owns stdout as a protocol channel — JSON-RPC for the
///                     MCP server, System.CommandLine for the commands — so nothing writes to
///                     <see cref="System.Console" /> directly.
///                 </item>
///                 <item>
///                     <c>di/no-captive-dependencies</c>: the all-singleton MCP host must not inject a
///                     scoped or transient service into a singleton (a forward ratchet — no such
///                     registration exists yet).
///                 </item>
///                 <item>
///                     <c>mcp/tools-accept-cancellation</c>: every <c>Task</c>-returning MCP tool method
///                     takes a <c>CancellationToken</c>, so a client cancel is honored and the idle
///                     watchdog can close.
///                 </item>
///                 <item>
///                     <c>roslyn/no-msbuildlocator-query</c>: no code queries
///                     <c>MSBuildLocator.QueryVisualStudioInstances</c> — on .NET hosts it returns no
///                     instances, so the sanctioned vswhere path is the only route.
///                 </item>
///                 <item>
///                     <c>mcp/no-blocking-waits</c>: the MCP pipeline never blocks on a task
///                     (<c>Wait</c>/<c>Result</c>/<c>GetResult</c>); the shutdown drain is the one
///                     sanctioned block.
///                 </item>
///                 <item>
///                     <c>naming/async-suffix</c>: <c>Task</c>- and <c>ValueTask</c>-returning methods
///                     carry the <c>Async</c> suffix across the union of the four shipping projects, with
///                     two named method exceptions.
///                 </item>
///                 <item>
///                     <c>mcp/warm-state-constructed-once</c>: only the server composition root constructs
///                     the single workspace session and fragment store the warm server holds for its
///                     lifetime.
///                 </item>
///                 <item>
///                     <c>roslyn/no-engine-types-on-seam</c>: the Roslyn project exposes no
///                     <c>Microsoft.Build.*</c> engine type on a public signature, preserving the
///                     runtime-bind split its csproj sets up.
///                 </item>
///                 <item>
///                     <c>xunit/leaf-adapter</c>: no product project references the xUnit adapter, so a
///                     test framework never ships to a package's consumers.
///                 </item>
///                 <item>
///                     <c>xunit/throws-setup-errors-only</c>: the adapter throws only
///                     <see cref="System.IO.FileNotFoundException" /> or
///                     <see cref="System.InvalidOperationException" />, its two documented setup errors.
///                 </item>
///             </list>
///         </item>
///         <item>
///             <b>Migrate</b> (<c>mcp/env-through-seam</c>): the MCP infrastructure still reaches for
///             <see cref="System.Environment" /> directly in a few places; the sanctioned path is the
///             <c>IEnvironment</c> seam (adapter <c>SystemEnvironment</c>). Grandfathered sites are captured;
///             the ratchet keeps any new infra type reaching for the static red.
///         </item>
///         <item>
///             <b>Quarantine</b> (<c>roslyn/msbuild-bootstrap</c>): the preview-VS MSBuild bootstrap is the
///             gnarliest code in the repo. Its interior is contained behind
///             <see cref="MsBuildBootstrap" />; the dragons prose records the load-bearing weirdness.
///         </item>
///     </list>
///     <para>
///         Anchor doctrine: in a self-spec, an expression member anchor (e.g.
///         <c>arch.Member&lt;Task&gt;(t =&gt; t.Wait())</c>) is real syntax — it mints a use edge attributed
///         to this spec class. A rule whose subject sweeps the spec assembly must therefore anchor with
///         <c>typeof</c> + <c>nameof</c> (nameof operands mint nothing), as
///         <c>roslyn/no-msbuildlocator-query</c> does; expression anchors are safe only under subjects that
///         exclude the spec assembly, as <c>mcp/no-blocking-waits</c> is — its subject is the MCP namespace,
///         which the spec class is not in.
///     </para>
/// </summary>
public sealed class LoadBearingArchSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("layering/core-no-roslyn")
            .Enforce(arch.Project("Zphil.LoadBearing")
                .MustNotReference(
                    arch.Project("Zphil.LoadBearing.Roslyn"),
                    arch.Namespace("Microsoft.CodeAnalysis.*"),
                    arch.Namespace("Microsoft.Build.*")))
            .Because("Core is the netstandard2.0 reified model both render targets consume; " +
                     "Roslyn extraction is host machinery, and a Microsoft.CodeAnalysis or Microsoft.Build " +
                     "package reference would leak compiler types into Core just as the project reference would.")
            .Fix("Depend on the Codebase model types in Core; keep Microsoft.CodeAnalysis behind " +
                 "Zphil.LoadBearing.Roslyn.");

        arch.Rule("cli/no-stdout")
            .Enforce(arch.Project("Zphil.LoadBearing.Cli")
                .MustNotUse(
                    arch.Member(() => Console.Out),
                    arch.Member(typeof(Console), nameof(Console.Write)),
                    arch.Member(() => Console.WriteLine())))
            .Because("Stdout is a protocol channel here — the MCP server speaks JSON-RPC over it and CLI " +
                     "output flows through System.CommandLine's console — so a direct Console write corrupts " +
                     "the wire and is invisible to the in-process tests.")
            .Fix("Write CLI output through the command's InvocationConfiguration console; route server " +
                 "diagnostics to the logger or Console.Error.");

        arch.Rule("di/no-captive-dependencies")
            .Enforce(arch.Registered(Lifetime.Singleton).InNamespace("Zphil.LoadBearing.*")
                .MustNotInject(arch.Registered(Lifetime.Scoped), arch.Registered(Lifetime.Transient)))
            .Because("The MCP server is one long-lived process wired all-singleton by design (IEnvironment, " +
                     "WorkspaceSession, SessionFragmentStore, ISolutionSource); a scoped or transient service " +
                     "injected into a singleton is captured for the whole process and silently shared across " +
                     "every tool call.")
            .Fix("Keep singletons depending only on singletons; resolve any scoped or transient work per call " +
                 "inside an IServiceScopeFactory scope instead of injecting it into the singleton.");

        arch.Rule("mcp/tools-accept-cancellation")
            .Enforce(arch.Namespace("Zphil.LoadBearing.Cli.Mcp.Tools.*")
                .Methods.Returning(typeof(Task), typeof(Task<>))
                .MustAcceptParameter(typeof(CancellationToken)))
            .Because("Tool calls run inside one long-lived server process; a tool method without a " +
                     "CancellationToken cannot honor a client cancel and holds the idle watchdog open.")
            .Fix("Add a trailing CancellationToken parameter (default it) and flow it into the runner.");

        arch.Rule("roslyn/no-msbuildlocator-query")
            .Enforce(arch.Types.InNamespace("Zphil.LoadBearing.*")
                .MustNotUse(arch.Member(typeof(MSBuildLocator), nameof(MSBuildLocator.QueryVisualStudioInstances))))
            .Because("On .NET hosts QueryVisualStudioInstances returns no VS Setup instances, so code " +
                     "consulting it silently picks the wrong MSBuild; the sanctioned path is vswhere " +
                     "(VsWhereLocator). The quarantine's dragons warn this in prose — this rule is the teeth.")
            .Fix("Go through MsBuildBootstrap / VsWhereLocator; never query the Locator for instances.");

        arch.Rule("mcp/no-blocking-waits")
            .Enforce(arch.Namespace("Zphil.LoadBearing.Cli.Mcp.*")
                .Except(arch.Types.WithNameMatching("ServerShutdown"))
                .MustNotUse(
                    arch.Member<Task>(t => t.Wait()),
                    arch.Member<Task<object>>(t => t.Result),
                    arch.Member<TaskAwaiter>(a => a.GetResult()),
                    arch.Member<TaskAwaiter<object>>(a => a.GetResult())))
            .Because("The MCP server multiplexes every tool call on one async JSON-RPC pipeline; a " +
                     "synchronous block inside it deadlocks the transport. ServerShutdown is the one " +
                     "sanctioned block — a bounded drain at process exit, with no pipeline left to starve.")
            .Fix("Await the task and flow the CancellationToken; blocking belongs only in ServerShutdown's drain.");

        arch.Rule("naming/async-suffix")
            .Enforce(arch.AnyOf(arch.Project("Zphil.LoadBearing"),
                    arch.Project("Zphil.LoadBearing.Roslyn"),
                    arch.Project("Zphil.LoadBearing.Cli"),
                    arch.Project("Zphil.LoadBearing.Xunit"))
                .Methods.Returning(typeof(Task), typeof(Task<>), typeof(ValueTask), typeof(ValueTask<>))
                .Where(m => m.Name != "Rule_Holds" && m.Name != "WhenAllCallsComplete",
                       description: "whose name is not Rule_Holds (a consumer-facing test display name) " +
                                    "or WhenAllCallsComplete (a Task.WhenAll-style combinator)")
                .MustHaveSuffix("Async"))
            .Because("House convention held repo-wide: an agent grepping *Async sees every await point; " +
                     "the two named exceptions are deliberate, not drift.")
            .Fix("Name Task-returning methods with the Async suffix.");

        arch.Rule("mcp/warm-state-constructed-once")
            .Enforce(arch.Project("Zphil.LoadBearing.Cli")
                .Except(arch.Types.WithNameMatching("McpServerCommand"))
                .MustNotConstruct(
                    arch.Type<WorkspaceSession>(),
                    arch.Types.WithNameMatching("SessionFragmentStore")))
            .Because("The warm server holds exactly one workspace session and one fragment store for " +
                     "its lifetime; a second construction forks the reconcile state and the caches " +
                     "silently diverge.")
            .Fix("Resolve them from DI; only McpServerCommand's composition root constructs them.");

        arch.Rule("roslyn/no-engine-types-on-seam")
            .Enforce(arch.Project("Zphil.LoadBearing.Roslyn")
                .MustNotExpose(arch.Namespace("Microsoft.Build.*")))
            .Because("MSBuild engine assemblies bind at runtime through the Locator (the csproj's " +
                     "ExcludeAssets=runtime split); an engine type on a public signature would force " +
                     "consumers to compile against MSBuild and break that split.")
            .Fix("Keep engine types behind internal members; hand callers repo-owned types like MsBuildSelection.");

        arch.Rule("xunit/leaf-adapter")
            .Enforce(arch.Project("Zphil.LoadBearing.Xunit")
                .MustNotBeReferencedBy(
                    arch.Project("Zphil.LoadBearing"),
                    arch.Project("Zphil.LoadBearing.Roslyn"),
                    arch.Project("Zphil.LoadBearing.Cli")))
            .Because("The adapter rides xunit.v3; a product reference would ship a test framework to " +
                     "every consumer of the referencing package.")
            .Fix("Keep the dependency one-way: the adapter consumes Core and Roslyn, never the reverse.");

        arch.Rule("xunit/throws-setup-errors-only")
            .Enforce(arch.Project("Zphil.LoadBearing.Xunit")
                .MustOnlyThrow(typeof(FileNotFoundException), typeof(InvalidOperationException)))
            .Because("The adapter runs inside consumers' test processes; its own failures must surface " +
                     "as the two documented setup errors, not as arbitrary exception noise beside the " +
                     "rule results.")
            .Fix("Route new failure modes through FileNotFoundException (missing solution) or " +
                 "InvalidOperationException (bad configuration).");

        arch.Rule("mcp/env-through-seam")
            .Migrate(
                "MCP infrastructure reads process env vars via System.Environment directly.",
                arch.Types.InNamespace("Zphil.LoadBearing.Cli.Mcp.Infrastructure.*")
                    .Except(arch.Types.WithNameMatching("SystemEnvironment"))
                    .MustNotReference(typeof(Environment)))
            .Because("A single IEnvironment seam keeps the MCP pipeline testable without mutating real " +
                     "process state.")
            .Fix("Inject IEnvironment (see SystemEnvironment); read via GetVariable.");

        arch.Scope("roslyn/msbuild-bootstrap")
            .Quarantine(arch.Namespace("Zphil.LoadBearing.Roslyn.MsBuild.*"))
            .BoundaryOnlyVia(typeof(MsBuildBootstrap))
            .Dragons("Preview VS MSBuild throws TypeInitializationException (XMakeElements) on " +
                     "legacy-namespace projects. We pick a stable VS 16/17 via vswhere and hand it to " +
                     "the out-of-process BuildHost through VSINSTALLDIR/VSCMD_VER=99.0. Do NOT switch " +
                     "to MSBuildLocator.QueryVisualStudioInstances — on .NET it returns no VS Setup " +
                     "instances.")
            .Because("Fragile host bootstrap; contain it behind MsBuildBootstrap.");
    }
}