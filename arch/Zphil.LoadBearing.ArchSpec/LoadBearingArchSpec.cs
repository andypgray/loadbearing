using System.Runtime.CompilerServices;
using Microsoft.Build.Locator;
using Zphil.LoadBearing.Packs.DotNet;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.MsBuild;

namespace Zphil.LoadBearing.ArchSpec;

/// <summary>
///     LoadBearing's own architecture spec — the dogfood render source, governing this repo's real code
///     so the product governs itself honestly. It exercises all three postures, eight declared layers,
///     and the verb families this codebase can honestly exercise — which is not all of them. The verb
///     ledger below names every remaining verb family with a reason, and a test holds it complete, so a
///     verb that ships without either a self-use or a ledger line reddens CI. Every rule below is a
///     genuine boundary: nothing in the build system prevents breaking it. The rendered block lives in the
///     committed root <c>AGENTS.md</c>, kept current by the self-spec tests.
///     <list type="bullet">
///         <item>
///             <b>Enforce</b> — the always-true laws.
///             <list type="bullet">
///                 <item>
///                     <c>layering/core-no-roslyn</c>: Core, the netstandard2.0 reified model both render
///                     targets consume, references neither the Extraction layer nor the
///                     <c>Microsoft.CodeAnalysis</c>/<c>Microsoft.Build</c> packages behind it (a package
///                     reference is the route the build cannot block).
///                 </item>
///                 <item>
///                     <c>layering/model-independent</c>: the Model references neither Checking nor
///                     Rendering — the product thesis (one reified model, two independent render targets)
///                     stated as law rather than as an intention.
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
///                     <c>di/no-service-locator</c> (from the <c>DotNetGuidance</c> pack): nothing in the
///                     CLI resolves a service from an <c>IServiceProvider</c> outside two sanctioned
///                     seams. <c>McpServerCommand</c> is the composition root. <c>GlobalCallToolFilter</c>
///                     is the second: <c>AddCallToolFilter</c> registers a delegate, so there is no
///                     constructor to inject into, and the request context is the only DI handle the SDK
///                     hands it. Both are named in the <c>Fix</c>, so a reader of a violation learns where
///                     resolving is allowed and why.
///                 </item>
///                 <item>
///                     <c>di/no-buildserviceprovider</c> (from the pack): a forward ratchet — nothing
///                     builds a second container while configuring services, and nothing does today.
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
///                     <c>mcp/no-blocking-waits</c>: nothing in the CLI or the extraction host blocks on a
///                     task (<c>Wait</c>/<c>Result</c>/<c>GetResult</c>) — a block there holds a thread-pool
///                     thread and drops the tool call's cancellation; the shutdown drain is the one
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
///                 <item>
///                     <c>packs/depends-on-core-only</c>: the rule pack is a leaf — it reaches for the
///                     Core vocabulary and nothing else, so taking it never drags the tool into a spec
///                     project's load context.
///                 </item>
///                 <item>
///                     <c>naming/interfaces</c>: every interface carries the <c>I</c> prefix, so <c>I*</c>
///                     stays a reliable grep for this codebase's seams.
///                 </item>
///                 <item>
///                     <c>model/constraint-nodes</c>: every <c>*Constraint</c> node lives in
///                     <c>Zphil.LoadBearing.Model</c>, where the evaluator and the sentence renderer each
///                     switch over the whole set. The public <c>Constraint</c> base is the one named
///                     exception — it belongs with the authoring surface.
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
///         The eight layers are a hybrid: five are assembly-shaped (Core, Extraction, Host, Adapter,
///         Pack), and three more — Model, Checking, Rendering — cut Core into the pieces
///         <c>layering/model-independent</c> needs to name. Checking and Rendering are declared for that
///         reason alone and carry no anchored rule, so they render a module-map row and no card; the same
///         honest negative the Quoting example shows. Layers are the spec's vocabulary here rather than a
///         second one beside <c>Project</c>, so the sentences read in layer voice throughout — including
///         <c>naming/async-suffix</c> and <c>mcp/no-blocking-waits</c>, whose union subjects anchor nothing
///         (a union has no single home directory) and so are documented negatives rather than more cards.
///     </para>
///     <para>
///         Two rules come from <c>DotNetGuidance</c>, the shared pack, and the rest of it is declined on
///         purpose — a pack is a menu, and not calling a method is the whole opt-out mechanism.
///         <c>exceptions/no-general-catch</c> is red here at around twenty sites that are deliberate
///         best-effort catches (a failed cache write is disposable derived data), so taking it at Migrate
///         would record debt that does not exist. <c>naming/async-suffix</c> and
///         <c>di/no-captive-dependencies</c> stay local, and the second is the finding worth keeping: the
///         pack cannot express this spec's two named method exceptions or its <c>ValueTask</c> return set,
///         and the MCP-specific rationale below names the actual singletons, which reads better than the
///         pack's general one. A rule's <c>Because</c> is not always universal.
///     </para>
///     <para>
///         Two more pack rules have local twins for that same reason. <c>mcp/no-blocking-waits</c> stands
///         in for <c>async/no-sync-over-async</c>, whose ban list also covers <c>GetAwaiter</c> itself and
///         whose prose is the general TAP one; the local rule names what a block actually costs here — a
///         thread-pool thread held for a child process's whole lifetime, and a tool call's
///         <c>CancellationToken</c> dropped, so a client cancel becomes a zombie — and sanctions
///         <c>ServerShutdown</c>'s drain by name. Its subject is the union of the two host-shaped layers
///         rather than the MCP namespace, because the code the server blocks in mostly is not in that
///         namespace: the git and vswhere launchers that could wedge a <c>--diff-base</c> tool call live in
///         the CLI's diff plumbing and in the extraction host.
///         <c>mcp/tools-accept-cancellation</c> stands in for
///         <c>async/accept-cancellation</c> on the same grounds: the stake here is a tool method holding
///         the idle watchdog open for the life of the server, not cancellation in general. Both could
///         have been pack calls with a narrow subject, since every pack method takes one, so what keeps
///         them local is the prose rather than the scope. The remaining two are declined as vacuous:
///         <c>http/reuse-httpclient</c> and <c>persistence/no-mapping-attributes</c> have nothing to
///         govern in a codebase that makes no HTTP calls and has no ORM.
///     </para>
///     <para>
///         The verb ledger. Every <c>Must*</c> verb this spec does not use is named here with its reason,
///         and a self-spec test holds the list complete against the public surface, so the ledger cannot
///         quietly rot as the vocabulary grows.
///         <c>MustNotCatch</c> is red at around twenty deliberate best-effort catches, reasoned above.
///         <c>MustBeSealed</c>, <c>MustBeAbstract</c>, <c>MustBeStatic</c>, <c>MustBePublic</c> and
///         <c>MustBeInternal</c> are the type-shape modals, and no layer here has a uniform shape. Two
///         were tried against the real code: the Model layer sealed is red at its five abstract bases,
///         and the Host layer internal is red at seven private nested helpers, because
///         <c>MustBeInternal</c> means exactly internal and private is not it. Each would need an
///         <c>Except</c> list longer than the rule, which is noise rather than a law.
///         <c>MustBePrivate</c> and <c>MustBeVirtual</c> are their member-level twins; nothing here
///         constrains a member's accessibility or virtuality.
///         <c>MustImplement</c>, <c>MustNotImplement</c>, <c>MustDeriveFrom</c> and
///         <c>MustNotDeriveFrom</c> are the hierarchy family, and the seams here are consumed by
///         injection rather than by inheritance. The one true statement available — every
///         <c>*Constraint</c> derives from <c>Constraint</c> — was tried and passes, but
///         <c>model/constraint-nodes</c> already governs that exact set, so landing it would widen the
///         verb range and say nothing new.
///         <c>MustBeAttributedWith</c> and <c>MustNotBeAttributedWith</c> hide the one rule this
///         repository wants and cannot have: an MCP tool type that does not carry
///         <c>[McpServerToolType]</c> silently fails to register, which is a real stake, but naming the
///         attribute would make this spec project reference the CLI and pull the whole tool and its
///         package closure into the load context of every spec run. Declined on that cost rather than
///         contrived around.
///         <c>MustHaveNameMatching</c> is the glob form of the naming family; the two naming laws here
///         are a prefix and a suffix, which say it more exactly.
///         <c>Must</c> is the predicate escape hatch for what the vocabulary cannot express, and nothing
///         here needs it — reaching for it ahead of the vocabulary would be the wrong instinct.
///     </para>
///     <para>
///         Beyond the verbs, three more surfaces are unused on purpose. Sugar twins of forms already
///         used — <c>arch.Type(typeof(X))</c> beside <c>arch.Type&lt;X&gt;()</c>,
///         <c>arch.AnyOf(Type…)</c> beside the selection overload, <c>arch.Registered()</c> beside the
///         lifetime-specific one — would exercise the API rather than this architecture.
///         <c>.Baseline(path)</c>, <c>.WhileYoureThere</c> and <c>.DragonsDoc</c> are unused because
///         their defaults <em>are</em> the intent here: the conventional baseline path, the default
///         boy-scout policy, and dragons prose short enough to live inline. And <c>baseline --add</c>,
///         the attributed-exception surface, has never been used here and structurally cannot be: an
///         attributed exception records a real waiver with a real reason, this repository has none to
///         record, and inventing one to exercise the verb is exactly the contrivance this ledger exists
///         to refuse.
///     </para>
///     <para>
///         Anchor doctrine: in a self-spec, an expression member anchor (e.g.
///         <c>arch.Member&lt;Task&gt;(t =&gt; t.Wait())</c>) is real syntax — it mints a use edge attributed
///         to this spec class. A rule whose subject sweeps the spec assembly must therefore anchor with
///         <c>typeof</c> + <c>nameof</c> (nameof operands mint nothing), as
///         <c>roslyn/no-msbuildlocator-query</c> does; expression anchors are safe only under subjects that
///         exclude the spec assembly, as <c>mcp/no-blocking-waits</c> is — its subject is the Host and
///         Extraction layers, and this spec class is in neither.
///     </para>
/// </summary>
public sealed class LoadBearingArchSpec : IArchitectureSpec
{
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

        arch.Rule("naming/async-suffix")
            .Enforce(arch.AnyOf(core, extraction, host, adapter, pack)
                .Methods.Returning(typeof(Task), typeof(Task<>), typeof(ValueTask), typeof(ValueTask<>))
                .Where(m => m.Name != "Rule_Holds" && m.Name != "WhenAllCallsComplete",
                    description: "whose name is not Rule_Holds (a consumer-facing test display name) " +
                                 "or WhenAllCallsComplete (a Task.WhenAll-style combinator)")
                .MustHaveSuffix("Async"))
            .Because("House convention held repo-wide: an agent grepping *Async sees every await point; " +
                     "the two named exceptions are deliberate, not drift.")
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
            .Dragons("Preview VS MSBuild throws TypeInitializationException (XMakeElements) on " +
                     "legacy-namespace projects. We pick a stable VS 16/17 via vswhere and hand it to " +
                     "the out-of-process BuildHost through VSINSTALLDIR/VSCMD_VER=99.0. Do NOT switch " +
                     "to MSBuildLocator.QueryVisualStudioInstances — on .NET it returns no VS Setup " +
                     "instances.")
            .Because("Fragile host bootstrap; contain it behind MsBuildBootstrap.");
    }
}