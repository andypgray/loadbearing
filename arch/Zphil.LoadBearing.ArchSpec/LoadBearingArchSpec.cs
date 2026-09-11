using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Microsoft.Build.Locator;
using Zphil.LoadBearing.Fluent;
using Zphil.LoadBearing.Packs.DotNet;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.MsBuild;

namespace Zphil.LoadBearing.ArchSpec;

/// <summary>
///     LoadBearing's own architecture spec — the dogfood render source, governing this repo's real code
///     so the product governs itself honestly. It exercises all four postures across eight declared
///     layers, and every rule below is a genuine boundary: nothing in the build system states these laws,
///     and almost nothing prevents breaking them — the release pipeline's four-package count and
///     locked-mode restore graze two of the packaging rules, each checking a downstream outcome rather
///     than the law. The rendered block lives in the committed root <c>AGENTS.md</c>, kept current by the self-spec
///     tests. Each rule carries its own law, <c>Because</c> and <c>Fix</c>, so this comment holds only
///     what the code cannot say.
///     <para>
///         Layers: five are assembly-shaped (Core, Extraction, Host, Adapter, Pack), and three more —
///         Model, Checking, Rendering — cut Core into the pieces <c>layering/model-independent</c> needs
///         to name. Every layer says what it is for, and a self-spec test holds that complete. Checking
///         and Rendering carry no anchored rule on purpose: a declared layer with no law of its own still
///         renders a module-map row — its name, its globs and its purpose — and no card, an honest
///         negative. The union-subject
///         rules (<c>naming/async-suffix</c>, <c>mcp/no-blocking-waits</c>,
///         <c>model/reified-nodes-immutable</c>, <c>state/no-static-mutable</c> and the exception laws)
///         place no card either — a union has no single home directory.
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
///         prefix and a suffix, which say it more exactly. <c>MustOnlyReferenceItself</c> idles because
///         Core is this solution's only reference-graph leaf and the temptation it actually faces is a
///         package: the leaf verb exempts external targets, so it cannot say the one thing
///         <c>layering/core-no-roslyn</c> exists to say, and a second rule beside that one would state
///         the weaker half twice. Its consumer is a modular monolith whose leaf module must stay clear
///         of its siblings. <c>MustBeRegistered</c> idles because nothing
///         here is registered by convention: the composition root wires a hand-written list of
///         infrastructure singletons, so a completeness rule over them could only restate that list at
///         itself — a tautology wearing a law's clothes. Its consumer is an estate where a naming
///         convention implies registration (every <c>*Handler</c>, say) and a type can carry the name
///         while missing the wiring. <c>MustHaveExactlyOneCounterpart</c> idles because this suite
///         organizes tests by behavior, not per type: 21 of the 110 public Core types have a
///         <c>{Name}Tests</c> class, and a rule demanding one each would grandfather the other 89 as
///         debt — recording a convention the tree deliberately does not follow as if it were merely
///         unpaid. Its consumer is an estate where the per-type test home or the per-service interface
///         is the convention, and a missing counterpart there is drift rather than design. The
///         predicate escape hatch is the one entry
///         that came off this list: the three <c>api/*-front-door</c> rules use it because a curated set
///         of names is the thing the vocabulary genuinely cannot say — no prefix, suffix or pattern picks
///         out "the types an author spells". The unused sugar overloads and the unused
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
    ///     The static fields whose mutability is load-bearing, exempted from <c>state/no-static-mutable</c>.
    ///     Keyed <c>{DeclaringType}.{Name}</c> rather than by type name, because unlike a broad catch a
    ///     second mutable static in the same class is exactly what this law should still catch. Three
    ///     families and nothing else:
    ///     <list type="bullet">
    ///         <item>
    ///             <b>Interlocked/Volatile locations</b> — <c>WorkspaceLoader._loadCount</c>,
    ///             <c>IdleTimeoutWatchdog.s_lastActivityTicks</c>, <c>IdleTimeoutWatchdog.s_inFlightCount</c>,
    ///             <c>ServerShutdown.s_hasExited</c>: a compare-and-swap needs a writable location, so
    ///             `readonly` is not expressible here, not merely unfashionable.
    ///         </item>
    ///         <item>
    ///             <b>Lock-guarded one-shot state</b> — <c>IdleTimeoutWatchdog.s_drainWaiter</c> (registered
    ///             and nulled under DrainLock) and <c>ArchRuleTests.s_run</c> (the per-closed-generic check
    ///             memo published under Gate; per-TSpec caching is the whole design).
    ///         </item>
    ///         <item>
    ///             <b>The clock seam</b> — <c>IdleTimeoutWatchdog.s_timestampProvider</c>: the only way to
    ///             test an idle timeout without sleeping for it.
    ///         </item>
    ///     </list>
    /// </summary>
    private static readonly HashSet<string> SanctionedMutableStatics =
    [
        "ArchRuleTests.s_run",
        "IdleTimeoutWatchdog.s_drainWaiter",
        "IdleTimeoutWatchdog.s_inFlightCount",
        "IdleTimeoutWatchdog.s_lastActivityTicks",
        "IdleTimeoutWatchdog.s_timestampProvider",
        "ServerShutdown.s_hasExited",
        "WorkspaceLoader._loadCount"
    ];

    /// <summary>
    ///     The curated root namespace of the contract package — what a spec author gets from
    ///     <c>using Zphil.LoadBearing;</c> and a dot. Three groups: the entry point and the spec interface,
    ///     the nouns and enums an author writes as arguments, and the nine static classes holding the
    ///     extension methods that are the verbs themselves. <c>IRuleBuilder</c>, <c>IScopeBuilder</c> and
    ///     <c>Member</c> are here because authors do name them — the validation corpus spells all three in
    ///     type position — even though a chain never has to. <c>ITypeInfo</c>, <c>IMemberInfo</c> and
    ///     <c>IProjectInfo</c> are here for the same reason: an escape-hatch predicate spells its
    ///     parameter type.
    /// </summary>
    private static readonly HashSet<string> CoreFrontDoor =
    [
        "Accessibility",
        "Arch",
        "Constraint",
        "FieldSelectionConstraints",
        "IArchitectureSpec",
        "IAttributeInfo",
        "IMemberInfo",
        "IParameterInfo",
        "IProjectInfo",
        "IRuleBuilder",
        "IScopeBuilder",
        "ITypeInfo",
        "Layer",
        "Lifetime",
        "Member",
        "MemberSelectionAdjectives",
        "MemberSelectionConstraints",
        "MethodSelectionConstraints",
        "MigrationPolicy",
        "Posture",
        "ProjectSelectionAdjectives",
        "ProjectSelectionConstraints",
        "PropertySelectionConstraints",
        "ScopeRole",
        "Selection",
        "SelectionAdjectives",
        "SelectionConstraints",
        "TypeKind"
    ];

    /// <summary>
    ///     The extraction host's seam: what the CLI and the xUnit adapter call to get a workspace, a
    ///     codebase model, or a refusal out of it. Everything else the project declares is machinery behind
    ///     this list and lives in one of its family namespaces.
    /// </summary>
    private static readonly HashSet<string> ExtractionFrontDoor =
    [
        "CodebaseExtractor",
        "LoadedSolution",
        "PathCanonicalizer",
        "SolutionDiscovery",
        "UserErrorException",
        "WorkspaceLoader",
        "WorkspaceSession",
        "WorkspaceSnapshot"
    ];

    /// <summary>
    ///     The tool's entry path: arguments to a command, a command to a runner, and an exception to an
    ///     exit code. <c>Program</c> is not listed because top-level statements synthesize it into the
    ///     global namespace, which this rule's subject does not reach; the self-spec's authored-types pin
    ///     is what holds it.
    /// </summary>
    private static readonly HashSet<string> HostFrontDoor =
    [
        "CliEntry",
        "CliErrorMapper",
        "CommandEntryPoint",
        "CommandFactory"
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
                "Zphil.LoadBearing.Fluent.*",
                "Zphil.LoadBearing.Hosting.*",
                "Zphil.LoadBearing.Internal.*",
                "Zphil.LoadBearing.Model.*",
                "Zphil.LoadBearing.Prose.*",
                "Zphil.LoadBearing.Rendering.*",
                "Zphil.LoadBearing.Validation.*")
            .Purpose("Core is the package a spec is written against: the fluent language, the model a spec " +
                     "compiles to, and the readers of that model that need no compiler.");
        Layer model = arch.Layer("Model", "Zphil.LoadBearing.Model.*")
            .Purpose("Model is the reified spec: the nodes a spec compiles to, and the one thing the checker and " +
                     "the renderers both read.");
        Layer checking = arch.Layer("Checking", "Zphil.LoadBearing.Checking.*")
            .Purpose("Checking evaluates each rule of the model against an extracted codebase: a verdict per rule, " +
                     "its violations, and what the baselines grandfather.");
        Layer rendering = arch.Layer("Rendering", "Zphil.LoadBearing.Rendering.*")
            .Purpose("Rendering turns the model and a check's results into what people and agents read: the " +
                     "managed block and cards, the diagrams, and the reports.");
        Layer extraction = arch.Layer("Extraction", "Zphil.LoadBearing.Roslyn.*")
            .Purpose("Extraction is the Roslyn host: it loads a solution through MSBuild and reads out the codebase " +
                     "model the checker evaluates against.");
        Layer host = arch.Layer("Host", "Zphil.LoadBearing.Cli.*")
            .Purpose("Host is the `loadbearing` command: the CLI verbs, the MCP server, and the spec loading and " +
                     "pipeline behind both.");
        Layer adapter = arch.Layer("Adapter", "Zphil.LoadBearing.Xunit.*")
            .Purpose("Adapter runs every rule of a spec as an individually named xUnit test in the consumer's own " +
                     "test project.");
        Layer pack = arch.Layer("Pack", "Zphil.LoadBearing.Packs.*")
            .Purpose("Pack is the shared rule pack: canonical .NET rules as an ordinary class library that a spec " +
                     "takes one method at a time.");

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

        arch.Rule("arch/no-ungoverned-types")
            .Enforce(arch.AnyOf(
                    arch.Project("Zphil.LoadBearing"),
                    arch.Project("Zphil.LoadBearing.Roslyn"),
                    arch.Project("Zphil.LoadBearing.Cli"),
                    arch.Project("Zphil.LoadBearing.Xunit"),
                    arch.Project("Zphil.LoadBearing.Packs.DotNet"))
                .Authored()
                .Except(arch.Types.Named("Program"))
                .MustBelongTo(core, extraction, host, adapter, pack))
            .Because("A type outside every declared layer is governed by nothing: no rule sweeps it, no card " +
                     "covers it, and check stays green while it accretes. The subject names the five projects " +
                     "rather than a namespace glob because a glob reaches only the namespaces someone " +
                     "predicted, and the failure this rule exists to catch is a type arriving under a root " +
                     "nobody did. The five assembly-shaped layers are the whole cover — Model, Checking and " +
                     "Rendering are cuts inside Core, not additions beside it. Two exemptions, both principled: " +
                     "generated types nobody can move, and Program, which top-level statements synthesize into " +
                     "the global namespace no glob can name.")
            .Fix("Put the type in a namespace one of the five layers covers, or add its namespace to a layer " +
                 "in this spec and say in review what the layer now means.");

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
                     "WorkspaceSession, SessionFragmentStore, SpecModelCache, SpecResolutionCache, " +
                     "ISolutionSource); a scoped or transient service injected into a singleton is captured " +
                     "for the whole process and silently shared across every tool call.")
            .Fix("Keep singletons depending only on singletons; resolve any scoped or transient work per call " +
                 "inside an IServiceScopeFactory scope instead of injecting it into the singleton.");

        DotNetGuidance.NoServiceLocator(arch, host,
            arch.Types.Named("McpServerCommand", "GlobalCallToolFilter"),
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

        arch.Rule("mcp/tool-types-in-cli")
            .Enforce(arch.Types.AttributedWith("ModelContextProtocol.Server.McpServerToolTypeAttribute")
                .MustResideInProject("Zphil.LoadBearing.Cli"))
            .Because("The converse of mcp/tool-types-attributed: that rule keeps every type in the tools " +
                     "namespace attributed, and this one keeps every attributed type in the CLI. The " +
                     "ModelContextProtocol SDK is the CLI's package reference alone, so a tool class declared " +
                     "in Core or Roslyn would drag the SDK into that package's closure — Core's rides into " +
                     "every spec project that references the contract library. The subject anchors on the " +
                     "attribute's fully-qualified name, and an empty subject fails loudly, so a rename in the " +
                     "SDK reds this rule rather than quietly emptying it.")
            .Fix("Move the tool class into Zphil.LoadBearing.Cli.Mcp.Tools beside ArchTools; only the CLI " +
                 "references the MCP SDK.");

        arch.Rule("roslyn/no-msbuildlocator-query")
            .Enforce(arch.Types.InNamespace("Zphil.LoadBearing.*")
                .MustNotUse(arch.Member(typeof(MSBuildLocator), nameof(MSBuildLocator.QueryVisualStudioInstances))))
            .Because("On .NET hosts QueryVisualStudioInstances returns no VS Setup instances, so code " +
                     "consulting it silently picks the wrong MSBuild; the sanctioned path is vswhere " +
                     "(VsWhereLocator). The quarantine's dragons warn this in prose — this rule is the teeth.")
            .Fix("Go through MsBuildBootstrap / VsWhereLocator; never query the Locator for instances.");

        arch.Rule("mcp/no-blocking-waits")
            .Enforce(arch.AnyOf(host, extraction)
                .Except(arch.Types.Named("ServerShutdown"))
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
                .Except(arch.Types.Named("SpecLoadContext"))
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
                .Except(arch.Types.Named("McpServerCommand"))
                .MustNotConstruct(
                    arch.Type<WorkspaceSession>(),
                    arch.Types.Named("SessionFragmentStore", "SpecModelCache", "SpecResolutionCache")))
            .Because("The warm server holds exactly one of each piece of session state for its lifetime — " +
                     "the workspace session, the fragment store, and the two spec caches that hang off the " +
                     "same load. A second construction forks the reconcile state and the caches silently " +
                     "diverge: the fork answers from an empty cache while the real one keeps ratcheting, so " +
                     "the cost the cache exists to remove comes back and nothing reports it.")
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

        // The seven broad handlers exempted below are the handlers that hold any failure and continue:
        // each is a boundary whose job is to absorb whatever arrives and carry on down a sanctioned
        // degraded path. The exemption is by type name, so it covers a type's future catches as well as
        // today's — the granularity a baseline entry would have, kept in the spec where it is read.
        //   ArchChecker          — the per-rule boundary: a fault evaluating one rule becomes that rule's
        //                          errored result, so the others still report.
        //   ArchRuleTests        — the discovery boundary: a spec that fails to build becomes one failing
        //                          test row rather than a silently empty theory.
        //   CommandEntryPoint    — the process boundary: every CLI failure becomes an exit code and a
        //                          message rather than a stack trace.
        //   IdleTimeoutWatchdog  — a background loop: a poll fault logs and disables the watchdog rather
        //                          than taking the server down.
        //   ParentProcessWatcher — a probe plus a background loop: both fail toward the safe direction for
        //                          a leak guard.
        //   ServerShutdown       — the drain: a faulting disposer must not stop the remaining disposers,
        //                          or the process exits holding a lock.
        //   VsWhereLocator       — a quarantined probe: any vswhere failure degrades to an empty instance
        //                          list and the MSBuildLocator.RegisterDefaults() fallback.
        arch.Rule("exceptions/no-swallowed-broad-catches")
            .Enforce(arch.AnyOf(core, extraction, host, adapter, pack)
                .Except(arch.Types.Named(
                    "ArchChecker",
                    "ArchRuleTests",
                    "CommandEntryPoint",
                    "IdleTimeoutWatchdog",
                    "ParentProcessWatcher",
                    "ServerShutdown",
                    "VsWhereLocator"))
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

        arch.Rule("state/no-static-mutable")
            .Enforce(arch.AnyOf(core, extraction, host, adapter, pack)
                .Authored()
                .Fields.ThatAreStatic()
                .Where(m => !SanctionedMutableStatics.Contains(m.DeclaringType.Name + "." + m.Name),
                    description: "that are not one of the sanctioned mutable statics (the Interlocked " +
                                 "locations, the lock-guarded one-shot state, and the clock seam)")
                .MustBeReadonly())
            .Because("A writable static is process-wide state in a library a long-lived MCP server calls " +
                     "concurrently and every spec project links: whoever wrote to it last decides what the " +
                     "next caller reads, and nothing in the type system says so. A const counts as readonly " +
                     "here — it is readonly's superset — so the law asks for the weakest thing that closes " +
                     "the hole. The subject is fields only; the two static `{ get; private set; }` " +
                     "diagnostics properties (MsBuildBootstrap.LastSelection, MsBuildGate.LastAcquisition) " +
                     "are outside it, and deliberately: a property-side twin is residue, not a gap here.")
            .Fix("Make it `readonly` or `const`. If it genuinely has to be written — an Interlocked location, " +
                 "a lock-guarded one-shot, or a test seam — add it to SanctionedMutableStatics in this spec " +
                 "with the family it belongs to, and say in review which one.");

        arch.Rule("packs/depends-on-core-only")
            .Enforce(pack.MustOnlyReference(core))
            .Because("A spec project takes the rule pack as a reference of its own, and the whole of what " +
                     "the pack needs is the Arch it composes onto. A reference to the extraction host or " +
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
            .Enforce(core.WithSuffix("Constraint").Except(typeof(Constraint))
                .MustResideInNamespace("Zphil.LoadBearing.Model.*"))
            .Because("A `*Constraint` is a node of the reified model — what a spec compiles to and what both " +
                     "render targets read back. The checker's dispatch and the sentence renderer each switch " +
                     "over the whole set, so a constraint node declared somewhere else is one they would " +
                     "silently not handle. `Constraint` itself is the public base a spec author names, so it " +
                     "stays in the root namespace with the rest of the authoring surface.")
            .Fix("Put the new constraint node in Zphil.LoadBearing.Model beside its siblings, and handle it " +
                 "in ConstraintEvaluator and SentenceRenderer.");

        // The adjective and noun hierarchies (SelectionAdjective, MemberAdjective, ProjectAdjective,
        // SelectionNoun) are internal and this project has no InternalsVisibleTo, so a typeof anchor
        // cannot reach them and they stay outside the union below. String anchors on their fully-qualified
        // names are the drop-in widening if review wants total cover: all four are already get-only, so it
        // would widen the subject and leave the verdict where it is. Not taken here — the string form is the
        // escape hatch for a type a spec project cannot reference at all, and these four are Core's own.
        arch.Rule("model/reified-nodes-immutable")
            .Enforce(arch.AnyOf(
                    arch.Types.DerivedFrom<Selection>(),
                    arch.Types.DerivedFrom<Constraint>(),
                    arch.Types.DerivedFrom<MemberSelection>(),
                    arch.Types.DerivedFrom<ProjectSelection>())
                .Properties.MustBeGetOnly())
            .Because("A spec compiles to these nodes and both render targets read them back, so a node is a " +
                     "value: the same spec must render the same sentence and check to the same verdict " +
                     "whoever walks it, and a settable property is the one way a walker could change what " +
                     "the next walker sees. The four roots are the whole reified hierarchy an author can " +
                     "name — a selection, a constraint, the member selection a projection mints, and the " +
                     "project selection the artifact noun mints — and their private-protected constructors " +
                     "mean no assembly outside Core can add a fifth. " +
                     "The BUILDERS are deliberately outside this law and stay mutable: a RuleRegistration " +
                     "accumulates a posture and a constraint across chained calls, which is what makes the " +
                     "stage machine a stage machine. They derive from none of these roots, so nothing here " +
                     "has to except them.")
            .Fix("Take the value in the constructor and expose it `{ get; }`. If a node genuinely has to " +
                 "accumulate, it is a builder, not a node — put it beside RuleRegistration.");

        arch.Rule("api/core-front-door")
            .Enforce(arch.Types.InNamespace("Zphil.LoadBearing")
                .Must(type => CoreFrontDoor.Contains(type.Name),
                    description: "be a type a spec author names"))
            .Because("The root namespace is the authoring surface. Someone writes `using Zphil.LoadBearing;` " +
                     "and what completion offers from there is, in practice, the whole language they believe " +
                     "exists — so a type that lands in it uninvited is one more thing to read past before " +
                     "finding the verb they wanted, and that cost falls in the first hour with the product. " +
                     "Hiding the rest is free: member resolution on a return type ignores using directives, " +
                     "so a chain keeps flowing through types nobody imports.")
            .Fix("Put the type where it belongs — Fluent for an intermediate a chain only passes through, " +
                 "Hosting for a node of the built model, Model/Checking/Rendering/Validation for the " +
                 "machinery behind them. If it really is a type an author spells, add its name to " +
                 "CoreFrontDoor in this spec and say in review what an author writes it for.");

        arch.Rule("api/extraction-front-door")
            .Enforce(arch.Types.InNamespace("Zphil.LoadBearing.Roslyn")
                .Must(type => ExtractionFrontDoor.Contains(type.Name),
                    description: "be a type the host calls across the extraction seam"))
            .Because("This package ships saying it is not for direct reference, which makes its root the " +
                     "seam the CLI and the adapter call and everything behind it free to be rearranged. A " +
                     "type left sitting in the root reads as callable, and the first caller that reaches " +
                     "past the seam converts the next refactor of the internals into a breaking change " +
                     "nobody signed up for.")
            .Fix("Put the type in Extraction, Diagnostics, Solutions, Hosting or Checking beside its " +
                 "siblings. If it genuinely belongs on the seam, add its name to ExtractionFrontDoor in " +
                 "this spec.");

        arch.Rule("api/host-front-door")
            .Enforce(arch.Types.InNamespace("Zphil.LoadBearing.Cli")
                .Must(type => HostFrontDoor.Contains(type.Name),
                    description: "be a type on the entry path"))
            .Because("Namespaces are not API here — this project ships as a tool — so the only thing the " +
                     "root namespace buys is legibility, and that is worth holding. The entry path is the " +
                     "first thing anyone reads to learn how the tool starts, and a root that also holds the " +
                     "verb requests, runners and pipeline stages it dispatches to hides that path among the " +
                     "thirty types it calls.")
            .Fix("Put the type in Verbs, Pipeline, SpecLoading or Rendering beside its siblings. If it is " +
                 "really part of the entry path, add its name to HostFrontDoor in this spec.");

        // The four packaging rules judge a project rather than any type in it, so their subjects are
        // project selections and their sites are lines in a project or props file. A broad one is bounded to `Zphil.*`
        // rather than left bare: the MyApp fixture solutions ride into this universe as passengers of the
        // spec fixtures' project references, and they are packable and unlocked, so an unbounded artifact
        // subject reds on code this repository does not ship and does not govern.
        arch.Rule("packaging/core-netstandard-only")
            .Enforce(arch.Projects.Named("Zphil.LoadBearing").MustOnlyTarget("netstandard2.0"))
            .Because("netstandard2.0 is the one TFM a net48 spec project and the net10 host can both load; " +
                     "Core is the contract every spec references, so its TFM is the product's reach.")
            .Fix("Anything needing a newer API belongs in Zphil.LoadBearing.Roslyn or the CLI, never Core.");

        arch.Rule("packaging/core-carries-nothing")
            .Enforce(arch.Projects.Named("Zphil.LoadBearing").MustReferenceNoPackages())
            .Because("Every consumer's spec csproj takes Core directly; a package Core drags along is imposed " +
                     "on every estate that adopts the tool, and a net48 spec has to resolve it too.")
            .Fix("Use what the netstandard2.0 surface already supplies, or put the dependency behind " +
                 "Zphil.LoadBearing.Roslyn or the CLI, where a package reference costs only the tool.");

        arch.Rule("packaging/shipping-locks-restore")
            .Enforce(arch.Projects.Matching("Zphil.*").Packable().MustLockPackages())
            .Because("CI restores locked; a shipping project without a lock file floats its dependency graph " +
                     "under audit.")
            .Fix("Set `RestorePackagesWithLockFile` to true and commit the packages.lock.json the next " +
                 "restore writes; src/Directory.Build.props already carries the policy for everything under it.");

        arch.Rule("packaging/only-the-four-ship")
            .Enforce(arch.Projects.Matching("Zphil.*")
                .Except(arch.Projects.Named(
                    "Zphil.LoadBearing",
                    "Zphil.LoadBearing.Roslyn",
                    "Zphil.LoadBearing.Xunit",
                    "Zphil.LoadBearing.Cli"))
                .MustNotBePackable())
            .Because("Exactly four packages ship, and the SDK packs by default, so a project that stays " +
                     "silent about it is one release-pipeline accident away from publishing this " +
                     "repository's internals under a name nobody reviewed.")
            .Fix("Set `IsPackable` to false in the project file. A project that genuinely should ship joins " +
                 "the four named in this rule's subject, and the release pipeline's package count, in the " +
                 "same change.");

        arch.Rule("mcp/env-through-seam")
            .Migrate(
                "MCP infrastructure reads process env vars via System.Environment directly.",
                arch.Types.InNamespace("Zphil.LoadBearing.Cli.Mcp.Infrastructure.*")
                    .Except(arch.Types.Named("SystemEnvironment"))
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

        // A caution, not a quarantine: every reader in Core reaches into Model, so there is no boundary to
        // state and nothing new to keep out. The weirdness is the sentence grammar itself, and the card is
        // what puts it in front of an agent before the edit that would tidy it away.
        arch.Scope("model/prose-fragments")
            .Caution(model)
            .Dragons("Every node here declares the prose fragment the renderers assemble into its sentence, and " +
                     "the fragments are position-blind: one phrase serves subject and reference position, so a " +
                     "fragment never closes its own parenthetical — the closing comma belongs to the junction " +
                     "(SentenceRenderer.EndsOpen and CloseBefore). A strict verb renders its strictness by the " +
                     "absence of the external-packages caveat; do not add one. A changed fragment moves its pin " +
                     "in the same commit.")
            .Because("Every reader in Core reaches into Model and every renderer assembles its fragments, so " +
                     "nothing here can be fenced; the hazard is an edit that reads as tidying.");
    }
}
