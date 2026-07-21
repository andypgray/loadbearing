using Zphil.LoadBearing.Roslyn.MsBuild;

namespace Zphil.LoadBearing.ArchSpec;

/// <summary>
///     LoadBearing's own architecture spec — the dogfood render source, exercising all three postures on
///     this repo's real code so the product governs itself honestly.
///     <list type="bullet">
///         <item>
///             <b>Enforce</b> — three boundaries. <c>layering/core-no-roslyn</c>: Core, the netstandard2.0
///             reified model both render targets consume, must reference neither the Roslyn extraction
///             project nor the <c>Microsoft.CodeAnalysis</c>/<c>Microsoft.Build</c> packages behind it (a
///             package reference is the route the build cannot block). <c>cli/no-stdout</c>: the CLI owns
///             stdout as a protocol channel — JSON-RPC for the MCP server, System.CommandLine for the
///             commands — so nothing writes to <see cref="System.Console" /> directly.
///             <c>di/no-captive-dependencies</c>: the all-singleton MCP host must not inject a scoped or
///             transient service into a singleton (a forward ratchet — no such registration exists yet).
///         </item>
///         <item>
///             <b>Migrate</b> (<c>mcp/env-through-seam</c>): the MCP infrastructure still reaches for
///             <see cref="System.Environment" /> directly in a few places; the sanctioned path is the
///             <c>IEnvironment</c> seam (adapter <c>SystemEnvironment</c>). Grandfathered sites are captured;
///             the ratchet keeps any new infra type reaching for the static red.
///         </item>
///         <item>
///             <b>Freeze</b> (<c>roslyn/msbuild-bootstrap</c>): the preview-VS MSBuild bootstrap is the
///             gnarliest code in the repo. Its interior is contained behind
///             <see cref="MsBuildBootstrap" />; the dragons prose records the load-bearing weirdness.
///         </item>
///     </list>
///     Nothing in the build system prevents breaking any of these, so each is a genuine boundary. The
///     rendered block lives in the committed root <c>AGENTS.md</c>, kept current by the self-spec tests.
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
            .Freeze(arch.Namespace("Zphil.LoadBearing.Roslyn.MsBuild.*"))
            .BoundaryOnlyVia(typeof(MsBuildBootstrap))
            .Dragons("Preview VS MSBuild throws TypeInitializationException (XMakeElements) on " +
                     "legacy-namespace projects. We pick a stable VS 16/17 via vswhere and hand it to " +
                     "the out-of-process BuildHost through VSINSTALLDIR/VSCMD_VER=99.0. Do NOT switch " +
                     "to MSBuildLocator.QueryVisualStudioInstances — on .NET it returns no VS Setup " +
                     "instances.")
            .Because("Fragile host bootstrap; contain it behind MsBuildBootstrap.");
    }
}