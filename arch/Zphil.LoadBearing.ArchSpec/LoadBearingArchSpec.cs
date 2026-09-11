using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Microsoft.Build.Locator;
using Zphil.LoadBearing.Fluent;
using Zphil.LoadBearing.Packs.DotNet;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.MsBuild;

namespace Zphil.LoadBearing.ArchSpec;

/// <summary>
///     LoadBearing's own architecture spec: the rules this repository is held to, over its real code. Each
///     is a boundary that nothing in the build system states and almost nothing else enforces, and the block
///     it renders into the committed root <c>AGENTS.md</c> is kept current by the self-spec tests.
///     Declaration order is reading order, the order the managed block and the scope cards render in, so
///     <see cref="Define" /> declares the layers and then calls one method per area, in the order they read;
///     the constants an area cites sit beside its method.
/// </summary>
public sealed class LoadBearingArchSpec : IArchitectureSpec
{
    /// <inheritdoc />
    public void Define(Arch arch)
    {
        // The unit of architecture here is the assembly, so each shipping layer is the project it is; the
        // three cuts inside Core are namespace cones, the pieces layering/model-independent needs to name.
        Layer core = arch.Layer("Core", arch.Project("Zphil.LoadBearing"))
            .Purpose("Core is the package a spec is written against: the fluent language, the model a spec " +
                     "compiles to, and the readers of that model that need no compiler.");
        Layer model = arch.Layer("Model", core.InNamespace("Zphil.LoadBearing.Model.*"))
            .Purpose("Model is the reified spec: the nodes a spec compiles to, and the one thing the checker and " +
                     "the renderers both read.");
        Layer checking = arch.Layer("Checking", core.InNamespace("Zphil.LoadBearing.Checking.*"))
            .Purpose("Checking evaluates each rule of the model against an extracted codebase: a verdict per rule, " +
                     "its violations, and what the baselines grandfather.");
        Layer rendering = arch.Layer("Rendering", core.InNamespace("Zphil.LoadBearing.Rendering.*"))
            .Purpose("Rendering turns the model and a check's results into what people and agents read: the " +
                     "managed block and cards, the diagrams, and the reports.");
        Layer extraction = arch.Layer("Extraction", arch.Project("Zphil.LoadBearing.Roslyn"))
            .Purpose("Extraction is the Roslyn host: it loads a solution through MSBuild and reads out the codebase " +
                     "model the checker evaluates against.");
        Layer host = arch.Layer("Host", arch.Project("Zphil.LoadBearing.Cli"))
            .Purpose("Host is the `loadbearing` command: the CLI verbs, the MCP server, and the spec loading and " +
                     "pipeline behind both.");
        Layer adapter = arch.Layer("Adapter", arch.Project("Zphil.LoadBearing.Xunit"))
            .Purpose("Adapter runs every rule of a spec as an individually named xUnit test in the consumer's own " +
                     "test project.");
        Layer pack = arch.Layer("Pack", arch.Project("Zphil.LoadBearing.Packs.DotNet"))
            .Purpose("Pack is the shared rule pack: canonical .NET rules as an ordinary class library that a spec " +
                     "takes one method at a time.");

        // `repo` is every type this repository declares, spec and test projects included; `shipping` is the
        // five layers that ship, which is `repo` minus those two.
        Selection repo = arch.Namespace("Zphil.LoadBearing.*");
        Selection shipping = arch.AnyOf(core, extraction, host, adapter, pack);

        Layering(arch, core, model, checking, rendering, extraction, host, adapter, pack);
        FrontDoors(arch);
        ModelNodes(arch, core);
        Packaging(arch);
        DependencyInjection(arch, host, repo);
        Cli(arch, host);
        McpServer(arch, host, extraction, repo);
        RoslynHost(arch, extraction, repo);
        Adapter(arch, adapter);
        Exceptions(arch, shipping);
        StaticState(arch, shipping);
        Naming(arch, shipping, repo);
        Scopes(arch, model);
    }

    private static void Layering(
        Arch arch, Layer core, Layer model, Layer checking, Layer rendering, Layer extraction, Layer host,
        Layer adapter, Layer pack)
    {
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

        arch.Rule("layering/no-circular-references")
            .Enforce(arch.Each(model, checking, rendering).MustNotHaveCircularReferences())
            .Because("Model, Checking and Rendering are three cuts inside one assembly with one intended direction: " +
                     "a model and two readers of it. A circle among them means a reader has become something the " +
                     "other reader depends on, and the next render target or checker change could no longer land " +
                     "without touching both.")
            .Fix("Move what a reader borrows from another reader down to where both can reach it: Model for " +
                 "shared data, a helper namespace for a helper. A reader depends on the model, never on the other reader.");

        arch.Rule("layering/leaves-independent")
            .Enforce(arch.Each(host, adapter, pack).MustNotReferenceEachOther())
            .Because("Host, Adapter and Pack are the three leaves of the project graph: each is a different way " +
                     "to consume Core, and none is a dependency of another. A reference between two leaves " +
                     "would pull one consumer's closure — the CLI's MSBuild and MCP machinery, the adapter's " +
                     "xunit, the pack's canonical rules — into a project that ships without it.")
            .Fix("Move the shared piece down into Core, or into Extraction if it needs Roslyn; a leaf takes " +
                 "what it needs from below, never from a sibling.");

        arch.Rule("layering/pack-depends-on-core-only")
            .Enforce(pack.MustOnlyReference(core))
            .Because("A spec project takes the rule pack as a reference of its own, and the whole of what " +
                     "the pack needs is the Arch it composes onto. A reference to the extraction host or " +
                     "the CLI would pull MSBuild and the rest of the tool into the closure of every spec " +
                     "project that takes the pack — and nothing in the project graph stops it, because the " +
                     "pack is a leaf and no reference out of it would be circular.")
            .Fix("Express the rule in the Core vocabulary the pack already has; anything that needs a " +
                 "workspace belongs in the tool, not in a pack.");

        arch.Rule("layering/adapter-is-a-leaf")
            .Enforce(adapter.MustNotBeReferencedBy(core, extraction))
            .Because("The adapter rides xunit.v3; a product reference would ship a test framework to " +
                     "every consumer of the referencing package. Host and Pack are the other two leaves, " +
                     "and layering/leaves-independent already holds all three off each other, so this rule " +
                     "names only what sits below the adapter.")
            .Fix("Keep the dependency one-way: the adapter consumes Core and Roslyn, never the reverse.");
    }

    private static void FrontDoors(Arch arch)
    {
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
    }

    /// <summary>
    ///     The curated root namespace of the contract package: what a spec author gets from
    ///     <c>using Zphil.LoadBearing;</c> and a dot. The entry point and the spec interface, the nouns and
    ///     enums an author writes as arguments, the static classes that hold the verbs, and the builder, info
    ///     and <c>Member</c> types an author spells in type position even though a chain never has to. Each
    ///     entry is a <c>nameof</c>, so a renamed or removed type cannot leave a stale name behind.
    /// </summary>
    private static readonly HashSet<string> CoreFrontDoor =
    [
        nameof(Accessibility),
        nameof(Arch),
        nameof(Constraint),
        nameof(FieldSelectionConstraints),
        nameof(IArchitectureSpec),
        nameof(IAttributeInfo),
        nameof(IMemberInfo),
        nameof(IParameterInfo),
        nameof(IProjectInfo),
        nameof(IRuleBuilder),
        nameof(IScopeBuilder),
        nameof(ITypeInfo),
        nameof(Layer),
        nameof(Lifetime),
        nameof(Member),
        nameof(MemberSelectionAdjectives),
        nameof(MemberSelectionConstraints),
        nameof(MethodSelectionConstraints),
        nameof(MigrationPolicy),
        nameof(Posture),
        nameof(ProjectSelectionAdjectives),
        nameof(ProjectSelectionConstraints),
        nameof(PropertySelectionConstraints),
        nameof(ScopeRole),
        nameof(Selection),
        nameof(SelectionAdjectives),
        nameof(SelectionConstraints),
        nameof(TypeKind)
    ];

    /// <summary>
    ///     The extraction host's seam: what the CLI and the xUnit adapter call to get a workspace, a codebase
    ///     model, or a refusal out of it. Everything else the project declares is machinery behind this list.
    /// </summary>
    private static readonly HashSet<string> ExtractionFrontDoor =
    [
        nameof(CodebaseExtractor),
        nameof(LoadedSolution),
        nameof(PathCanonicalizer),
        nameof(SolutionDiscovery),
        nameof(UserErrorException),
        nameof(WorkspaceLoader),
        nameof(WorkspaceSession),
        nameof(WorkspaceSnapshot)
    ];

    /// <summary>
    ///     The tool's entry path: arguments to a command, a command to a runner, and an exception to an exit
    ///     code, named as strings because the CLI is not a reference of this project. <c>Program</c> is
    ///     absent because top-level statements synthesize it into the global namespace, which this rule's
    ///     subject does not reach; a self-spec test holds it instead.
    /// </summary>
    private static readonly HashSet<string> HostFrontDoor =
    [
        "CliEntry",
        "CliErrorMapper",
        "CommandEntryPoint",
        "CommandFactory"
    ];

    private static void ModelNodes(Arch arch, Layer core)
    {
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

        // The internal adjective and noun hierarchies stay outside this union: a typeof anchor cannot reach
        // them without InternalsVisibleTo, and the string form is kept for types a spec cannot reference at all.
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
                     "stage machine a stage machine.")
            .Fix("Take the value in the constructor and expose it `{ get; }`. If a node genuinely has to " +
                 "accumulate, it is a builder, not a node — put it beside RuleRegistration.");
    }

    private static void Packaging(Arch arch)
    {
        // The packaging rules judge projects, so their sites are lines in a project or props file. Their
        // broad subjects are bounded to `Zphil.*`: the MyApp fixture solutions ride into this universe as
        // passengers of the spec fixtures' project references, packable and unlocked, and a bare artifact
        // subject would red on code this repository does not ship.
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
            .Citation("https://learn.microsoft.com/nuget/consume-packages/package-references-in-project-files#locking-dependencies")
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
    }

    private static void DependencyInjection(Arch arch, Layer host, Selection repo)
    {
        arch.Rule("di/no-captive-dependencies")
            .Enforce(arch.Registered(Lifetime.Singleton).InNamespace("Zphil.LoadBearing.*")
                .MustNotInject(arch.Registered(Lifetime.Scoped), arch.Registered(Lifetime.Transient)))
            .Because("The MCP server is one long-lived process wired all-singleton by design (IEnvironment, " +
                     "WorkspaceSession, SessionFragmentStore, SpecModelCache, SpecResolutionCache, " +
                     "ISolutionSource); a scoped or transient service injected into a singleton is captured " +
                     "for the whole process and silently shared across every tool call.")
            .Citation("https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines")
            .Fix("Keep singletons depending only on singletons; resolve any scoped or transient work per call " +
                 "inside an IServiceScopeFactory scope instead of injecting it into the singleton.");

        // Two rules come from the shared pack; the rest of it is declined, since a pack is a menu and not
        // calling a method is the whole opt-out.
        DotNetGuidance.NoServiceLocator(arch, host,
            arch.Types.Named("McpServerCommand", "GlobalCallToolFilter"),
            PackPosture.Enforce,
            "Take the dependency in the constructor; McpServerCommand's composition root and GlobalCallToolFilter's request context are the only sanctioned resolve sites.");

        DotNetGuidance.NoBuildServiceProvider(arch, repo, PackPosture.Enforce);
    }

    private static void Cli(Arch arch, Layer host)
    {
        arch.Rule("cli/no-stdout")
            .Enforce(host
                .MustNotUse(
                    arch.Member(() => Console.Out),
                    arch.Member(typeof(Console), nameof(Console.Write)),
                    arch.Member(() => Console.WriteLine())))
            .Because("Stdout is a protocol channel here — the MCP server speaks JSON-RPC over it and CLI " +
                     "output flows through System.CommandLine's console — so a direct Console write corrupts " +
                     "the wire and is invisible to the in-process tests.")
            .Citation("https://modelcontextprotocol.io/specification/2025-06-18/basic/transports#stdio")
            .Fix("Write CLI output through the command's InvocationConfiguration console; route server " +
                 "diagnostics to the logger or Console.Error.");
    }

    private static void McpServer(Arch arch, Layer host, Layer extraction, Selection repo)
    {
        arch.Rule("mcp/tools-accept-cancellation")
            .Enforce(repo
                .Methods.AttributedWith(McpServerToolAttribute)
                .MustAcceptParameter<CancellationToken>())
            .Because("Tool calls run inside one long-lived server process; a tool method without a " +
                     "CancellationToken cannot honor a client cancel and holds the idle watchdog open.")
            .Citation("https://modelcontextprotocol.io/specification/2025-06-18/basic/utilities/cancellation")
            .Fix("Add a trailing CancellationToken parameter (default it) and flow it into the runner.");

        arch.Rule("mcp/tool-types-attributed")
            .Enforce(arch.Namespace("Zphil.LoadBearing.Cli.Mcp.Tools.*")
                .MustBeAttributedWith(McpServerToolTypeAttribute))
            .Because("Tool discovery is the attribute walk: WithCoercingTools registers the [McpServerTool] " +
                     "methods of [McpServerToolType]-annotated classes, so a tool class without the type " +
                     "attribute compiles, registers nothing, and its tools silently vanish from the server.")
            .Fix("Put [McpServerToolType] on the tool class (see ArchTools), and keep tool classes in " +
                 "Zphil.LoadBearing.Cli.Mcp.Tools.");

        arch.Rule("mcp/tool-types-in-cli")
            .Enforce(arch.Types.AttributedWith(McpServerToolTypeAttribute)
                .MustResideInProject("Zphil.LoadBearing.Cli"))
            .Because("The converse of mcp/tool-types-attributed: that rule keeps every type in the tools " +
                     "namespace attributed, and this one keeps every attributed type in the CLI. The " +
                     "ModelContextProtocol SDK is the CLI's package reference alone, so a tool class declared " +
                     "in Core or Roslyn would drag the SDK into that package's closure — Core's rides into " +
                     "every spec project that references the contract library.")
            .Fix("Move the tool class into Zphil.LoadBearing.Cli.Mcp.Tools beside ArchTools; only the CLI " +
                 "references the MCP SDK.");

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
            .Citation("https://learn.microsoft.com/dotnet/csharp/asynchronous-programming/async-scenarios")
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

        arch.Rule("mcp/env-through-seam")
            .Enforce(arch.Types.InNamespace("Zphil.LoadBearing.Cli.Mcp.Infrastructure.*")
                .Except(arch.Types.Named("SystemEnvironment"))
                .MustNotUse(
                    arch.Member(typeof(Environment), nameof(Environment.GetEnvironmentVariable)),
                    arch.Member(typeof(Environment), nameof(Environment.GetEnvironmentVariables)),
                    arch.Member(typeof(Environment), nameof(Environment.ExpandEnvironmentVariables)),
                    arch.Member(typeof(Environment), nameof(Environment.SetEnvironmentVariable))))
            .Because("A single IEnvironment seam keeps the MCP pipeline testable without mutating real " +
                     "process state: a test sets a variable on the fake, never on the process.")
            .Fix("Inject IEnvironment (see SystemEnvironment); read via GetVariable.");
    }

    /// <summary>
    ///     The MCP SDK's tool attribute by full name. The SDK is the CLI's package reference alone, so the
    ///     spec cannot name its types, and an empty subject fails loudly, so a rename there reds the rules
    ///     anchored on these names rather than quietly emptying them.
    /// </summary>
    private const string McpServerToolAttribute = "ModelContextProtocol.Server.McpServerToolAttribute";

    /// <summary>The type-level twin of <see cref="McpServerToolAttribute" />, which marks a tool class.</summary>
    private const string McpServerToolTypeAttribute = "ModelContextProtocol.Server.McpServerToolTypeAttribute";

    private static void RoslynHost(Arch arch, Layer extraction, Selection repo)
    {
        arch.Rule("roslyn/no-msbuildlocator-query")
            .Enforce(repo
                .MustNotUse(arch.Member(typeof(MSBuildLocator), nameof(MSBuildLocator.QueryVisualStudioInstances))))
            .Because("On .NET hosts QueryVisualStudioInstances returns no VS Setup instances, so code " +
                     "consulting it silently picks the wrong MSBuild; the sanctioned path is vswhere " +
                     "(VsWhereLocator). The quarantine's dragons warn this in prose — this rule is the teeth.")
            .Fix("Go through MsBuildBootstrap / VsWhereLocator; never query the Locator for instances.");

        arch.Rule("roslyn/no-engine-types-on-seam")
            .Enforce(extraction.MustNotExpose(arch.Namespace("Microsoft.Build.*")))
            .Because("MSBuild engine assemblies bind at runtime through the Locator (the csproj's " +
                     "ExcludeAssets=runtime split); an engine type on a public signature would force " +
                     "consumers to compile against MSBuild and break that split.")
            .Fix("Keep engine types behind internal members; hand callers repo-owned types like MsBuildSelection.");
    }

    private static void Adapter(Arch arch, Layer adapter)
    {
        arch.Rule("xunit/throws-setup-errors-only")
            .Enforce(adapter.MustOnlyThrow(typeof(FileNotFoundException), typeof(InvalidOperationException)))
            .Because("The adapter runs inside consumers' test processes; its own failures must surface " +
                     "as the two documented setup errors, not as arbitrary exception noise beside the " +
                     "rule results.")
            .Fix("Route new failure modes through FileNotFoundException (missing solution) or " +
                 "InvalidOperationException (bad configuration).");
    }

    private static void Exceptions(Arch arch, Selection shipping)
    {
        // The seven exempted handlers each hold any failure and continue down a sanctioned degraded path:
        //   ArchChecker           one rule's fault becomes that rule's errored result, so the others still report
        //   ArchRuleTests         a spec that fails to build becomes one failing test row, not an empty theory
        //   CommandEntryPoint     every CLI failure becomes an exit code and a message, not a stack trace
        //   IdleTimeoutWatchdog   a poll fault logs and disables the watchdog rather than take the server down
        //   ParentProcessWatcher  a probe and a loop that both fail toward the safe side for a leak guard
        //   ServerShutdown        a faulting disposer must not stop the rest, or the process exits holding a lock
        //   VsWhereLocator        any vswhere failure degrades to an empty list and the RegisterDefaults fallback
        arch.Rule("exceptions/no-swallowed-broad-catches")
            .Enforce(shipping
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
            .Citation("https://learn.microsoft.com/dotnet/standard/design-guidelines/using-standard-exception-types")
            .Fix("Rethrow after the cleanup, translate to a type that names the failure, or add a `when` " +
                 "filter naming the exceptions this handler is for. A handler that genuinely has to hold " +
                 "everything and continue, like a process boundary or a background loop, goes on the spec's " +
                 "sanctioned list instead.");

        arch.Rule("exceptions/no-bare-bcl-throws")
            .Enforce(shipping
                .MustNotThrow(typeof(Exception), typeof(SystemException), typeof(ApplicationException)))
            .Because("A bare BCL exception type says only that something went wrong. A caller cannot filter " +
                     "on it, so throwing one forces every handler above into the swallowed broad catch " +
                     "`exceptions/no-swallowed-broad-catches` exists to prevent, and leaves a message string " +
                     "as the only thing left to match on. Nothing here throws one today; this rule is what " +
                     "stops the first one arriving quietly.")
            .Citation("https://learn.microsoft.com/dotnet/standard/design-guidelines/using-standard-exception-types")
            .Fix("Throw a type that names the failure — one of this repo's own error types, or the closest " +
                 "BCL type such as InvalidOperationException or IOException — so a handler can filter on it.");
    }

    private static void StaticState(Arch arch, Selection shipping)
    {
        // The subject is fields only. The two static `{ get; private set; }` diagnostics properties
        // (MsBuildBootstrap.LastSelection, MsBuildGate.LastAcquisition) are outside it, and deliberately:
        // a property-side twin is residue, not a gap here.
        arch.Rule("state/no-static-mutable")
            .Enforce(shipping
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
                     "the hole.")
            .Fix("Make it `readonly` or `const`. If it genuinely has to be written — an Interlocked location, " +
                 "a lock-guarded one-shot, or a test seam — add it to SanctionedMutableStatics in this spec " +
                 "with the family it belongs to, and say in review which one.");
    }

    /// <summary>
    ///     The static fields whose mutability is load-bearing, exempted from <c>state/no-static-mutable</c>.
    ///     Keyed by declaring type and name rather than by type alone, because a second mutable static in the
    ///     same class is exactly what the law should still catch.
    /// </summary>
    private static readonly HashSet<string> SanctionedMutableStatics =
    [
        // Interlocked and volatile locations: a compare-and-swap needs a writable field.
        "IdleTimeoutWatchdog.s_inFlightCount",
        "IdleTimeoutWatchdog.s_lastActivityTicks",
        "ServerShutdown.s_hasExited",
        "WorkspaceLoader._loadCount",
        // One-shot state written under a lock.
        "ArchRuleTests.s_run",
        "IdleTimeoutWatchdog.s_drainWaiter",
        // The clock seam: the only way to test an idle timeout without sleeping for it.
        "IdleTimeoutWatchdog.s_timestampProvider"
    ];

    private static void Naming(Arch arch, Selection shipping, Selection repo)
    {
        arch.Rule("naming/async-suffix")
            .Enforce(shipping
                .Authored()
                .Methods.Returning(typeof(Task), typeof(Task<>), typeof(ValueTask), typeof(ValueTask<>))
                .Where(m => m.Name != "Rule_Holds" && m.Name != "Workspace_LoadedCompletely"
                                                   && m.Name != "WhenAllCallsComplete",
                    description: "whose name is not Rule_Holds or Workspace_LoadedCompletely (consumer-facing " +
                                 "test display names) or WhenAllCallsComplete (a Task.WhenAll-style combinator)")
                .MustHaveSuffix("Async"))
            .Because("House convention held repo-wide: an agent grepping *Async sees every await point; " +
                     "the three named exceptions are deliberate, not drift.")
            .Citation("https://learn.microsoft.com/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap")
            .Fix("Name Task-returning methods with the Async suffix.");

        arch.Rule("naming/interfaces")
            .Enforce(repo.OfKind(TypeKind.Interface).MustHavePrefix("I"))
            .Because("Nearly every seam here is an interface — ISolutionSource, IEnvironment, " +
                     "IArchitectureSpec — and the `I` prefix is what lets a reader or an agent tell the port " +
                     "from its implementation at a glance, and what makes `I*` a reliable grep for the " +
                     "seams. A convention that holds for most types tells you nothing; this one is total.")
            .Citation("https://learn.microsoft.com/dotnet/standard/design-guidelines/names-of-classes-structs-and-interfaces")
            .Fix("Give the interface the `I` prefix.");
    }

    private static void Scopes(Arch arch, Layer model)
    {
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
