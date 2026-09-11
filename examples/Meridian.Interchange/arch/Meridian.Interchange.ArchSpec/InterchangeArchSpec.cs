using Meridian.Interchange.Outbox;
using Meridian.Interchange.Partners;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Zphil.LoadBearing;
using Zphil.LoadBearing.Packs.DotNet;

namespace Meridian.Interchange.ArchSpec;

/// <summary>
///     The interchange subsystem's architecture spec — Meridian's outbound worker that drains an
///     outbox and transmits booking confirmations, status updates, and customs filings to trading
///     partners over HTTP. Its rules encode canonical Microsoft .NET guidance: partner clients are
///     wired through the composition root, HttpClients come from the factory, nothing resolves from
///     the provider outside that root, the hosted service scopes its own work, base Exception is
///     swallowed only in a top-level handler, and awaitable-returning methods carry the Async suffix
///     — with the one legacy manifest corner that still blocks grandfathered until its gateway goes
///     async.
///     <para>
///         Nine of the twelve are canonical .NET guidance that nothing here makes special, so they
///         come from the shared <c>DotNetGuidance</c> pack rather than being written again: the pack
///         owns each rule's <c>Because</c>, this spec chooses the posture and the selections, and five
///         of the nine override the <c>Fix</c> because remediation names real types
///         (<c>CarrierClient</c>, <c>ScopedDispatchRunner</c>, <c>OutboxDispatcher</c>). The other
///         three name domain types and stay here, where they belong.
///     </para>
/// </summary>
public sealed class InterchangeArchSpec : IArchitectureSpec
{
    /// <inheritdoc />
    public void Define(Arch arch)
    {
        Selection interchange = arch.Types.InNamespace("Meridian.Interchange.*");
        Selection host = arch.Namespace("Meridian.Interchange.Host.*");

        // 1 — DI guidelines: no direct instantiation of dependent classes outside the composition root.
        arch.Rule("di/construct-via-container")
            .Enforce(arch.Types.Except(host).MustNotConstruct(arch.Types.Implementing<IPartnerClient>()))
            .Because("Partner clients are wired with their pooled HttpClient and options in the composition root; constructing one elsewhere bypasses that wiring and the registered lifetime.")
            .Citation("https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines")
            .Fix("Inject IPartnerClient (or IEnumerable<IPartnerClient>); the composition root owns wiring.");

        // 2 — HttpClient guidelines: reuse via IHttpClientFactory; new HttpClient() exhausts sockets.
        DotNetGuidance.ReuseHttpClient(arch, arch.Types, host, PackPosture.Enforce,
            "Take a typed or named client from IHttpClientFactory; see how CarrierClient receives its HttpClient.");

        // 3 — DI guidelines antipattern: do not resolve from IServiceProvider where DI would inject.
        DotNetGuidance.NoServiceLocator(arch, arch.Types, host, PackPosture.Enforce);

        // 4 — DI guidelines: avoid BuildServiceProvider while configuring services.
        DotNetGuidance.NoBuildServiceProvider(arch, arch.Types, PackPosture.Enforce);

        // 5 — Async scenarios / ASP.NET best practices: replace .Wait()/.Result with await; do not block.
        DotNetGuidance.NoSyncOverAsync(arch, arch.Types,
            PackPosture.Migrate("A legacy manifest gateway exposes only synchronous calls, so the adapter blocks on async work."),
            "Await the call and make the method async; the legacy corner is grandfathered until the SDK exposes async.");

        // 6 — Scoped-service tutorial + Options lifetimes: a singleton BackgroundService must not capture scoped services.
        arch.Rule("di/hosted-services-scope-their-work")
            .Enforce(arch.Types.DerivedFrom<BackgroundService>().MustNotReference(typeof(IOptionsSnapshot<>), typeof(IOutboxStore)))
            .Because("A BackgroundService is a singleton; a captured scoped IOptionsSnapshot or scoped store outlives its scope; resolve per work item from an IServiceScopeFactory scope.")
            .Citation("https://learn.microsoft.com/dotnet/core/extensions/scoped-service")
            .Fix("Inject IServiceScopeFactory, create a scope per iteration, resolve scoped services inside it; see OutboxDispatcher and ScopedDispatchRunner.");

        // 7 — DI guidelines antipattern: a singleton must not capture a scoped or transient service (the general captive-dependency form).
        DotNetGuidance.NoCaptiveDependencies(arch, arch.Registered(Lifetime.Singleton), PackPosture.Enforce,
            "Resolve the scoped or transient service per unit of work inside an IServiceScopeFactory scope, as ScopedDispatchRunner does; take only singleton-safe dependencies in the constructor.");

        // 8 — TAP (normative): Task-returning methods carry the Async suffix.
        DotNetGuidance.AsyncSuffix(arch, interchange, PackPosture.Enforce);

        // 9 — Standard exception types (FDG): catch base Exception only in a top-level handler; the dispatcher's poll loop is the one sanctioned catch-all.
        DotNetGuidance.NoGeneralCatch(arch, interchange,
            arch.Types.DerivedFrom<BackgroundService>(), PackPosture.Enforce,
            "Catch the specific exception you can handle; the only sanctioned catch-all is the dispatcher's poll loop, where OutboxDispatcher logs and continues to the next poll.");

        // 10 — TAP: a Task-returning method accepts a CancellationToken so callers can cancel and flow the request down the chain.
        DotNetGuidance.AcceptCancellation(arch, interchange, PackPosture.Enforce,
            "Add a CancellationToken parameter and flow OutboxDispatcher's stoppingToken through the call chain, as ScopedDispatchRunner and OutboxProcessor already do.");

        // 11 — Architectural principles (persistence ignorance): a persisted type carries no ORM mapping attribute; validation DataAnnotations are untouched.
        DotNetGuidance.NoMappingAttributes(arch, interchange, PackPosture.Enforce);

        // 12 — CQRS reads (ViewModels/DTOs made for the consumer): a persisted entity must not surface on the public partner contract; hand partners a DTO.
        arch.Rule("contracts/no-entity-exposure")
            .Enforce(interchange.Except(arch.Namespace("Meridian.Interchange.Outbox.*"))
                .MustNotExpose(typeof(OutboxMessage)))
            .Because("Exposing a persisted entity on a public signature couples partner-facing code to the storage model, so a change to how a message is persisted reshapes the partner contract; hand partners a DTO made for the wire instead.")
            .Citation("https://learn.microsoft.com/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/cqrs-microservice-reads")
            .Fix("Map the message to a PartnerEnvelope at the OutboxProcessor boundary and expose that; keep OutboxMessage inside the Outbox module.");
    }
}
