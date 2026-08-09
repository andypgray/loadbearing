using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Microsoft.Build.Locator;
using Zphil.LoadBearing.Packs.DotNet;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.MsBuild;

namespace Zphil.LoadBearing.ArchSpec;

/// <summary>
///     LoadBearing's own architecture spec — the dogfood render source, governing this repo's real code
///     so the product governs itself honestly. It exercises all three postures across eight declared
///     layers, and every rule below is a genuine boundary: nothing in the build system prevents breaking
///     it. The rendered block lives in the committed root <c>AGENTS.md</c>, kept current by the self-spec
///     tests. Each rule carries its own law, <c>Because</c> and <c>Fix</c>, so this comment holds only
///     what the code cannot say.
///     <para>
///         Layers: five are assembly-shaped (Core, Extraction, Host, Adapter, Pack), and three more —
///         Model, Checking, Rendering — cut Core into the pieces <c>layering/model-independent</c> needs
///         to name. Checking and Rendering carry no anchored rule on purpose: a declared layer with
///         nothing to say renders a module-map row and no card, an honest negative. The union-subject
///         rules (<c>naming/async-suffix</c>, <c>mcp/no-blocking-waits</c> and the exception laws) place
///         no card either — a union has no single home directory.
///     </para>
///     <para>
///         Two rules come from <c>DotNetGuidance</c>, the shared pack, and the rest of it is declined on
///         purpose — a pack is a menu, and not calling a method is the whole opt-out mechanism. Two of
///         its rules have nothing to govern in a codebase with no HTTP calls and no ORM; the others are
///         declined for local twins whose <c>Because</c> names what the failure actually costs here.
///     </para>
///     <para>
///         The verb ledger. Every <c>Must*</c> verb this spec does not use is named here with its
///         reason, and a self-spec test holds the list complete against the public surface, so the
///         ledger cannot quietly rot as the vocabulary grows. <c>MustNotCatch</c> and
///         <c>MustNotCatchUnfiltered</c> are the unrefined forms on the catch axis; the law here is
///         <c>MustNotSwallow</c>, which passes the house catch shapes both of them would red.
///         <c>MustBeSealed</c>, <c>MustBeAbstract</c>, <c>MustBeStatic</c>, <c>MustBePublic</c> and
///         <c>MustBeInternal</c> found no layer with a uniform type shape — the two tried against the
///         real code each went red on legitimate members and needed an <c>Except</c> list longer than
///         the rule — and their member-level twins <c>MustBePrivate</c> and <c>MustBeVirtual</c>
///         constrain nothing here either. <c>MustImplement</c>, <c>MustNotImplement</c>,
///         <c>MustDeriveFrom</c> and <c>MustNotDeriveFrom</c> idle because the seams here are consumed
///         by injection rather than inheritance, and the one true hierarchy statement is already
///         governed by <c>model/constraint-nodes</c>. <c>MustNotBeAttributedWith</c> idles because no
///         attribute is forbidden here, and inventing a ban to exercise a verb is the contrivance this
///         ledger refuses. <c>MustHaveNameMatching</c> idles because the two naming laws here are a
///         prefix and a suffix, which say it more exactly. <c>Must</c>, the predicate escape hatch,
///         idles because nothing here defeats the vocabulary. The unused sugar overloads and the unused
///         <c>.Baseline(path)</c>, <c>.WhileYoureThere</c> and <c>.DragonsDoc</c> surfaces are the same
///         story: their defaults are the intent here, and exercising an API for its own sake is not
///         dogfood.
///     </para>
///     <para>
///         Anchor doctrine, for anyone editing this file: in a self-spec, an expression member anchor
///         (e.g. <c>arch.Member&lt;Task&gt;(t =&gt; t.Wait())</c>) is real syntax — it mints a use edge
///         attributed to this spec class. A rule whose subject sweeps the spec assembly must anchor with
///         <c>typeof</c> + <c>nameof</c> (nameof operands mint nothing), as
///         <c>roslyn/no-msbuildlocator-query</c> does; expression anchors are safe only under subjects
///         that exclude the spec assembly, as <c>mcp/no-blocking-waits</c>'s host-and-extraction
///         subject does.
///     </para>
/// </summary>
public sealed class LoadBearingArchSpec : IArchitectureSpec
{
    /// <summary>
    ///     The types whose broad catches deliberately hold any failure and continue, exempted from
    ///     <c>exceptions/no-swallowed-broad-catches</c>. One kind of handler belongs here and nothing else:
    ///     a boundary whose job is to absorb whatever arrives and carry on down a sanctioned degraded path.
    ///     <list type="bullet">
    ///         <item>
    ///             <c>CommandEntryPoint</c> — the process boundary: every CLI failure becomes an exit code
    ///             and a message rather than a stack trace.
    ///         </item>
    ///         <item>
    ///             <c>ArchChecker</c> — the per-rule boundary: a fault evaluating one rule becomes that
    ///             rule's errored result, so the others still report.
    ///         </item>
    ///         <item>
    ///             <c>ArchRuleTests</c> — the discovery boundary: a spec that fails to build becomes one
    ///             failing test row rather than a silently empty theory.
    ///         </item>
    ///         <item>
    ///             <c>IdleTimeoutWatchdog</c> — a background loop: a poll fault logs and disables the
    ///             watchdog rather than taking the server down.
    ///         </item>
    ///         <item>
    ///             <c>ParentProcessWatcher</c> — a probe plus a background loop: both fail toward the safe
    ///             direction for a leak guard.
    ///         </item>
    ///         <item>
    ///             <c>ServerShutdown</c> — the drain: a faulting disposer must not stop the remaining
    ///             disposers, or the process exits holding a lock.
    ///         </item>
    ///         <item>
    ///             <c>VsWhereLocator</c> — a quarantined probe: any vswhere failure degrades to an empty
    ///             instance list and the <c>MSBuildLocator.RegisterDefaults()</c> fallback.
    ///         </item>
    ///     </list>
    ///     The exemption is by type name, so it covers a type's future catches as well as today's — the
    ///     granularity a baseline entry would have, kept in the spec where it is read.
    /// </summary>
    private static readonly HashSet<string> SanctionedBroadCatchers =
    [
        "ArchChecker",
        "ArchRuleTests",
        "CommandEntryPoint",
        "IdleTimeoutWatchdog",
        "ParentProcessWatcher",
        "ServerShutdown",
        "VsWhereLocator"
    ];

    /// <inheritdoc />
    public void Define(Arch arch)
    {
        // The five assembly-shaped layers plus three inside Core, in module-map order. Core's globs are
        // its namespace inventory rather than a subtree, because Core's root namespace is also this
        // repo's root namespace — `Zphil.LoadBearing.*` would swallow Roslyn, Cli, Xunit and the pack.
        // That makes the list brittle by construction, which is why SelfSpecTests pins it against the
        // project: a new Core namespace that nobody adds here would silently escape core's rules.
        Layer core = arch.Layer("Core",
            "Zphil.LoadBearing",
            "Zphil.LoadBearing.Baselines.*",
            "Zphil.LoadBearing.Building.*",
            "Zphil.LoadBearing.Checking.*",
            "Zphil.LoadBearing.Codebase.*",
            "Zphil.LoadBearing.Discovery.*",
            "Zphil.LoadBearing.Internal.*",
            "Zphil.LoadBearing.Model.*",
            "Zphil.LoadBearing.Prose.*",
            "Zphil.LoadBearing.Rendering.*",
            "Zphil.LoadBearing.Validation.*");
        Layer model = arch.Layer("Model", "Zphil.LoadBearing.Model.*");
        Layer checking = arch.Layer("Checking", "Zphil.LoadBearing.Checking.*");
        Layer rendering = arch.Layer("Rendering", "Zphil.LoadBearing.Rendering.*");
        Layer extraction = arch.Layer("Extraction", "Zphil.LoadBearing.Roslyn.*");
        Layer host = arch.Layer("Host", "Zphil.LoadBearing.Cli.*");
        Layer adapter = arch.Layer("Adapter", "Zphil.LoadBearing.Xunit.*");
        Layer pack = arch.Layer("Pack", "Zphil.LoadBearing.Packs.*");

        arch.Rule("layering/core-no-roslyn")
            .Enforce(core.MustNotReference(
                extraction,
                arch.Namespace("Microsoft.CodeAnalysis.*"),
                arch.Namespace("Microsoft.Build.*")))
            .Because("Core is the netstandard2.0 reified model both render targets consume; " +
                     "Roslyn extraction is host machinery, and a Microsoft.CodeAnalysis or Microsoft.Build " +
                     "package reference would leak compiler types into Core just as the project reference would.")
            .Fix("Depend on the Codebase model types in Core; keep Microsoft.CodeAnalysis behind " +
                 "Zphil.LoadBearing.Roslyn.");

        arch.Rule("layering/model-independent")
            .Enforce(model.MustNotReference(checking, rendering))
            .Because("The Model is the one reified thing this product is built around: a spec compiles to it, " +
                     "and checking and rendering are two independent readers of it. A reference the other way " +
                     "would make the model know about a consumer, and the next render target could no longer be " +
                     "added without touching it.")
            .Fix("Keep the dependency one-way: give Model the data, and let Checking or Rendering read it.");

        arch.Rule("cli/no-stdout")
            .Enforce(host
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

        DotNetGuidance.NoServiceLocator(arch, host,
            arch.AnyOf(arch.Types.WithNameMatching("McpServerCommand"), arch.Types.WithNameMatching("GlobalCallToolFilter")),
            PackPosture.Enforce,
            "Take the dependency in the constructor; McpServerCommand's composition root and GlobalCallToolFilter's request context are the only sanctioned resolve sites.");

        DotNetGuidance.NoBuildServiceProvider(arch, arch.Types.InNamespace("Zphil.LoadBearing.*"), PackPosture.Enforce);

        arch.Rule("mcp/tools-accept-cancellation")
            .Enforce(arch.Namespace("Zphil.LoadBearing.*")
                .Methods.AttributedWith("ModelContextProtocol.Server.McpServerToolAttribute")
                .MustAcceptParameter(typeof(CancellationToken)))
            .Because("Tool calls run inside one long-lived server process; a tool method without a " +
                     "CancellationToken cannot honor a client cancel and holds the idle watchdog open.")
            .Fix("Add a trailing CancellationToken parameter (default it) and flow it into the runner.");

        arch.Rule("mcp/tool-types-attributed")
            .Enforce(arch.Namespace("Zphil.LoadBearing.Cli.Mcp.Tools.*")
                .MustBeAttributedWith("ModelContextProtocol.Server.McpServerToolTypeAttribute"))
            .Because("Tool discovery is the attribute walk: WithCoercingTools registers the [McpServerTool] " +
                     "methods of [McpServerToolType]-annotated classes, so a tool class without the type " +
                     "attribute compiles, registers nothing, and its tools silently vanish from the server.")
            .Fix("Put [McpServerToolType] on the tool class (see ArchTools), and keep tool classes in " +
                 "Zphil.LoadBearing.Cli.Mcp.Tools.");

        arch.Rule("roslyn/no-msbuildlocator-query")
            .Enforce(arch.Types.InNamespace("Zphil.LoadBearing.*")
                .MustNotUse(arch.Member(typeof(MSBuildLocator), nameof(MSBuildLocator.QueryVisualStudioInstances))))
            .Because("On .NET hosts QueryVisualStudioInstances returns no VS Setup instances, so code " +
                     "consulting it silently picks the wrong MSBuild; the sanctioned path is vswhere " +
                     "(VsWhereLocator). The quarantine's dragons warn this in prose — this rule is the teeth.")
            .Fix("Go through MsBuildBootstrap / VsWhereLocator; never query the Locator for instances.");

        arch.Rule("mcp/no-blocking-waits")
            .Enforce(arch.AnyOf(host, extraction)
                .Except(arch.Types.WithNameMatching("ServerShutdown"))
                .MustNotUse(
                    arch.Member<Task>(t => t.Wait()),
                    arch.Member<Task<object>>(t => t.Result),
                    arch.Member<TaskAwaiter>(a => a.GetResult()),
                    arch.Member<TaskAwaiter<object>>(a => a.GetResult())))
            .Because("The CLI and the extraction host are one long-lived MCP server as often as they are a " +
                     "one-shot command, and a blocking wait there costs twice: it holds a thread-pool thread " +
                     "for as long as the child process or workspace load it waits on, and it drops the " +
                     "CancellationToken the tool call was handed, so a client cancel leaves a zombie running " +
                     "the timeout out instead. ServerShutdown is the one sanctioned block — a bounded drain " +
                     "at process exit, with nothing left to starve.")
            .Fix("Await the task and flow the CancellationToken. Where a call chain genuinely cannot be " +
                 "async — the MSBuild registration path is JIT-quarantined behind a synchronous seam — wait " +
                 "on the resource itself, a process handle rather than a Task.");

        arch.Rule("mcp/no-path-assembly-loads")
            .Enforce(host
                .Except(arch.Types.WithNameMatching("SpecLoadContext"))
                .MustNotUse(arch.Member(typeof(AssemblyLoadContext), nameof(AssemblyLoadContext.LoadFromAssemblyPath))))
            .Because("A host that loads an assembly from its path pins that file for as long as the host " +
                     "lives, and this host lives for a whole session: the model roots the spec's Types, so " +
                     "the collectible context never collects and the operating system never releases the " +
                     "build output. Every rebuild of what was loaded then fails, and the only cure is killing " +
                     "the server — which for a stdio server is terminal, because the client never reconnects " +
                     "one, so the per-edit check that was the reason to run it goes quietly dead. " +
                     "SpecLoadContext is the single sanctioned caller: it reads bytes, and reaches for the " +
                     "loader only when the file is already gone.")
            .Fix("Read the assembly's bytes and load from the stream, the way " +
                 "SpecLoadContext.LoadWithoutLocking does; never hand a path to the loader from the CLI.");

        arch.Rule("naming/async-suffix")
            .Enforce(arch.AnyOf(core, extraction, host, adapter, pack)
                .Authored()
                .Methods.Returning(typeof(Task), typeof(Task<>), typeof(ValueTask), typeof(ValueTask<>))
                .Where(m => m.Name != "Rule_Holds" && m.Name != "Workspace_LoadedCompletely"
                                                   && m.Name != "WhenAllCallsComplete",
                    description: "whose name is not Rule_Holds or Workspace_LoadedCompletely (consumer-facing " +
                                 "test display names) or WhenAllCallsComplete (a Task.WhenAll-style combinator)")
                .MustHaveSuffix("Async"))
            .Because("House convention held repo-wide: an agent grepping *Async sees every await point; " +
                     "the three named exceptions are deliberate, not drift.")
            .Fix("Name Task-returning methods with the Async suffix.");

        arch.Rule("mcp/warm-state-constructed-once")
            .Enforce(host
                .Except(arch.Types.WithNameMatching("McpServerCommand"))
                .MustNotConstruct(
                    arch.Type<WorkspaceSession>(),
                    arch.Types.WithNameMatching("SessionFragmentStore")))
            .Because("The warm server holds exactly one workspace session and one fragment store for " +
                     "its lifetime; a second construction forks the reconcile state and the caches " +
                     "silently diverge.")
            .Fix("Resolve them from DI; only McpServerCommand's composition root constructs them.");

        arch.Rule("roslyn/no-engine-types-on-seam")
            .Enforce(extraction.MustNotExpose(arch.Namespace("Microsoft.Build.*")))
            .Because("MSBuild engine assemblies bind at runtime through the Locator (the csproj's " +
                     "ExcludeAssets=runtime split); an engine type on a public signature would force " +
                     "consumers to compile against MSBuild and break that split.")
            .Fix("Keep engine types behind internal members; hand callers repo-owned types like MsBuildSelection.");

        arch.Rule("xunit/leaf-adapter")
            .Enforce(adapter.MustNotBeReferencedBy(core, extraction, host, pack))
            .Because("The adapter rides xunit.v3; a product reference would ship a test framework to " +
                     "every consumer of the referencing package.")
            .Fix("Keep the dependency one-way: the adapter consumes Core and Roslyn, never the reverse.");

        arch.Rule("xunit/throws-setup-errors-only")
            .Enforce(adapter.MustOnlyThrow(typeof(FileNotFoundException), typeof(InvalidOperationException)))
            .Because("The adapter runs inside consumers' test processes; its own failures must surface " +
                     "as the two documented setup errors, not as arbitrary exception noise beside the " +
                     "rule results.")
            .Fix("Route new failure modes through FileNotFoundException (missing solution) or " +
                 "InvalidOperationException (bad configuration).");

        arch.Rule("exceptions/no-swallowed-broad-catches")
            .Enforce(arch.AnyOf(core, extraction, host, adapter, pack)
                .Where(t => !SanctionedBroadCatchers.Contains(t.Name),
                    description: "whose name is not one of the sanctioned broad handlers (the process, rule " +
                                 "and test boundaries, the background loops, and the quarantined probes — " +
                                 "the handlers that hold any failure and continue on a sanctioned degraded " +
                                 "path)")
                .MustNotSwallow(typeof(Exception)))
            .Because("A broad catch that holds the failure and continues holds the ones nobody thought " +
                     "about — a cancellation, an out-of-memory, the bug introduced two lines up — and hands " +
                     "the caller a wrong answer that reads like a right one. Two shapes are not that, and " +
                     "this rule passes both: a `when` filter is where a handler writes down what it is " +
                     "actually for, so everything else keeps travelling (`catch (Exception ex) when (ex is " +
                     "IOException or UnauthorizedAccessException)`); and a clause that ends in a `throw` — " +
                     "cleanup-and-rethrow, or translate-and-throw — suppresses nothing at all. The " +
                     "sanctioned handlers are exempt by type name rather than by site, the same granularity " +
                     "a baseline would give, stated in the spec instead of recorded in a file.")
            .Fix("Rethrow after the cleanup, translate to a type that names the failure, or add a `when` " +
                 "filter naming the exceptions this handler is for. A handler that genuinely has to hold " +
                 "everything and continue, like a process boundary or a background loop, goes on the spec's " +
                 "sanctioned list instead.");

        arch.Rule("exceptions/no-bare-bcl-throws")
            .Enforce(arch.AnyOf(core, extraction, host, adapter, pack)
                .MustNotThrow(typeof(Exception), typeof(SystemException), typeof(ApplicationException)))
            .Because("A bare BCL exception type says only that something went wrong. A caller cannot filter " +
                     "on it, so throwing one forces every handler above into the swallowed broad catch " +
                     "`exceptions/no-swallowed-broad-catches` exists to prevent, and leaves a message string " +
                     "as the only thing left to match on. Nothing here throws one today; this rule is what " +
                     "stops the first one arriving quietly.")
            .Fix("Throw a type that names the failure — one of this repo's own error types, or the closest " +
                 "BCL type such as InvalidOperationException or IOException — so a handler can filter on it.");

        arch.Rule("packs/depends-on-core-only")
            .Enforce(pack.MustOnlyReference(core, pack))
            .Because("The rule pack ships as its own package for spec projects to reference, and the whole " +
                     "of what it needs is the Arch it composes onto. A reference to the extraction host or " +
                     "the CLI would pull MSBuild and the rest of the tool into the closure of every spec " +
                     "project that takes the pack — and nothing in the project graph stops it, because the " +
                     "pack is a leaf and no reference out of it would be circular.")
            .Fix("Express the rule in the Core vocabulary the pack already has; anything that needs a " +
                 "workspace belongs in the tool, not in a pack.");

        arch.Rule("naming/interfaces")
            .Enforce(arch.Types.InNamespace("Zphil.LoadBearing.*").OfKind(TypeKind.Interface).MustHavePrefix("I"))
            .Because("Nearly every seam here is an interface — ISolutionSource, IEnvironment, " +
                     "IArchitectureSpec — and the `I` prefix is what lets a reader or an agent tell the port " +
                     "from its implementation at a glance, and what makes `I*` a reliable grep for the " +
                     "seams. A convention that holds for most types tells you nothing; this one is total.")
            .Fix("Give the interface the `I` prefix.");

        arch.Rule("model/constraint-nodes")
            .Enforce(core.WithSuffix("Constraint").Except(arch.Type<Constraint>())
                .MustResideInNamespace("Zphil.LoadBearing.Model.*"))
            .Because("A `*Constraint` is a node of the reified model — what a spec compiles to and what both " +
                     "render targets read back. The checker's dispatch and the sentence renderer each switch " +
                     "over the whole set, so a constraint node declared somewhere else is one they would " +
                     "silently not handle. `Constraint` itself is the public base a spec author names, so it " +
                     "stays in the root namespace with the rest of the authoring surface.")
            .Fix("Put the new constraint node in Zphil.LoadBearing.Model beside its siblings, and handle it " +
                 "in ConstraintEvaluator and SentenceRenderer.");

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
            .Dragons("We pick a VS 16/17 via vswhere and hand it to the out-of-process BuildHost through " +
                     "VSINSTALLDIR/VSCMD_VER=99.0. That preference is caution about a moving target, not a " +
                     "live workaround: the TypeInitializationException (XMakeElements) on legacy-namespace " +
                     "projects came from an early VS 18 preview and does not reproduce on VS 18.6. Where no " +
                     "16/17 is installed, the highest available is taken and MsBuildBootstrap.LastSelection " +
                     "says so — the CLI prints it beside any workspace-load diagnostic, and " +
                     "LOADBEARING_VS_INSTALL_PATH overrides the choice. Do NOT switch to " +
                     "MSBuildLocator.QueryVisualStudioInstances — on .NET it returns no VS Setup instances.")
            .Because("Fragile host bootstrap; contain it behind MsBuildBootstrap.");
    }
}
