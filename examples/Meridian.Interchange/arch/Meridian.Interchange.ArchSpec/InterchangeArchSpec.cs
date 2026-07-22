using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.CompilerServices;
using Meridian.Interchange.Outbox;
using Meridian.Interchange.Partners;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Zphil.LoadBearing;

namespace Meridian.Interchange.ArchSpec;

/// <summary>
///     The interchange subsystem's architecture spec — Meridian's outbound worker that drains an
///     outbox and transmits booking confirmations, status updates, and customs filings to trading
///     partners over HTTP. Its rules encode canonical Microsoft .NET guidance: partner clients are
///     wired through the composition root, HttpClients come from the factory, nothing resolves from
///     the provider outside that root, the hosted service scopes its own work, base Exception is
///     caught only in a top-level handler, and Task-returning methods carry the Async suffix — with
///     the one legacy manifest corner that still blocks
///     grandfathered until its gateway goes async.
/// </summary>
public sealed class InterchangeArchSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        Selection host = arch.Namespace("Meridian.Interchange.Host.*");

        // 1 — DI guidelines: no direct instantiation of dependent classes outside the composition root.
        arch.Rule("di/construct-via-container")
            .Enforce(arch.Types.Except(host).MustNotConstruct(arch.Types.Implementing(typeof(IPartnerClient))))
            .Because("Partner clients are wired with their pooled HttpClient and options in the composition root; constructing one elsewhere bypasses that wiring and the registered lifetime — https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines")
            .Fix("Inject IPartnerClient (or IEnumerable<IPartnerClient>); the composition root owns wiring.");

        // 2 — HttpClient guidelines: reuse via IHttpClientFactory; new HttpClient() exhausts sockets.
        arch.Rule("http/reuse-httpclient")
            .Enforce(arch.Types.Except(host).MustNotConstruct(typeof(HttpClient)))
            .Because("A new HttpClient per call exhausts sockets under load; IHttpClientFactory pools handlers — https://learn.microsoft.com/dotnet/fundamentals/networking/http/httpclient-guidelines")
            .Fix("Take a typed or named client from IHttpClientFactory; see how CarrierClient receives its HttpClient.");

        // 3 — DI guidelines antipattern: do not resolve from IServiceProvider where DI would inject.
        arch.Rule("di/no-service-locator")
            .Enforce(arch.Types.Except(host).MustNotUse(
                arch.Member<IServiceProvider>(sp => sp.GetService(typeof(object))),
                arch.Member(typeof(ServiceProviderServiceExtensions), nameof(ServiceProviderServiceExtensions.GetService)),
                arch.Member(typeof(ServiceProviderServiceExtensions), nameof(ServiceProviderServiceExtensions.GetRequiredService))))
            .Because("Resolving services from IServiceProvider at call sites hides a type's real dependencies; declare them as constructor parameters — https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines")
            .Fix("Take the dependency in the constructor; the composition root and its scope seam are the only sanctioned resolve sites.");

        // 4 — DI guidelines: avoid BuildServiceProvider while configuring services.
        arch.Rule("di/no-buildserviceprovider")
            .Enforce(arch.Types.MustNotUse(
                arch.Member(typeof(ServiceCollectionContainerBuilderExtensions), nameof(ServiceCollectionContainerBuilderExtensions.BuildServiceProvider))))
            .Because("Calling BuildServiceProvider while configuring services builds a second container with its own singletons — a duplicate-instance trap — https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines")
            .Fix("Register the dependency and let the host build the provider once; inject what you need.");

        // 5 — Async scenarios / ASP.NET best practices: replace .Wait()/.Result with await; do not block.
        arch.Rule("async/no-sync-over-async")
            .Migrate(
                "A legacy manifest gateway exposes only synchronous calls, so the adapter blocks on async work.",
                arch.Types.MustNotUse(
                    arch.Member<Task>(t => t.Wait()),
                    arch.Member<Task<object>>(t => t.Result),
                    arch.Member<Task>(t => t.GetAwaiter()),
                    arch.Member<Task<object>>(t => t.GetAwaiter()),
                    arch.Member(typeof(TaskAwaiter), nameof(TaskAwaiter.GetResult)),
                    arch.Member(typeof(TaskAwaiter<>), nameof(TaskAwaiter<object>.GetResult))))
            .Because("Blocking on a Task (.Result/.Wait/.GetResult) ties up a thread and can deadlock in a captured context; await instead — https://learn.microsoft.com/dotnet/csharp/asynchronous-programming/async-scenarios")
            .Fix("Await the call and make the method async; the legacy corner is grandfathered until the SDK exposes async.");

        // 6 — Scoped-service tutorial + Options lifetimes: a singleton BackgroundService must not capture scoped services.
        arch.Rule("di/hosted-services-scope-their-work")
            .Enforce(arch.Types.DerivedFrom<BackgroundService>().MustNotReference(typeof(IOptionsSnapshot<>), typeof(IOutboxStore)))
            .Because("A BackgroundService is a singleton; a captured scoped IOptionsSnapshot or scoped store outlives its scope — resolve per work item from an IServiceScopeFactory scope — https://learn.microsoft.com/dotnet/core/extensions/scoped-service")
            .Fix("Inject IServiceScopeFactory, create a scope per iteration, resolve scoped services inside it; see OutboxDispatcher and ScopedDispatchRunner.");

        // 7 — DI guidelines antipattern: a singleton must not capture a scoped or transient service (the general captive-dependency form).
        arch.Rule("di/no-captive-dependencies")
            .Enforce(arch.Registered(Lifetime.Singleton).MustNotInject(
                arch.Registered(Lifetime.Scoped),
                arch.Registered(Lifetime.Transient)))
            .Because("A singleton is created once and holds every dependency it injects for the whole process, so a scoped or transient service injected into it is captured past its lifetime and shared across all callers — https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines")
            .Fix("Resolve the scoped or transient service per unit of work inside an IServiceScopeFactory scope, as ScopedDispatchRunner does; take only singleton-safe dependencies in the constructor.");

        // 8 — TAP (normative): Task-returning methods carry the Async suffix.
        arch.Rule("naming/async-suffix")
            .Enforce(arch.Types.InNamespace("Meridian.Interchange.*").Methods.Returning(typeof(Task), typeof(Task<>)).MustHaveSuffix("Async"))
            .Because("Task-returning methods carry the Async suffix so callers see at the call site that a method must be awaited — https://learn.microsoft.com/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap")
            .Fix("Rename the method to end in Async.");

        // 9 — Standard exception types (FDG): catch base Exception only in a top-level handler; the dispatcher's poll loop is the one sanctioned catch-all.
        arch.Rule("exceptions/no-general-catch")
            .Enforce(arch.Types.InNamespace("Meridian.Interchange.*").Except(arch.Types.DerivedFrom<BackgroundService>())
                .MustNotCatch(typeof(Exception)))
            .Because("Catching base Exception outside a top-level handler swallows the faults you meant to see; the dispatcher's poll loop is that handler, so scope the catch-all there and let other code catch only the specific types it can handle — https://learn.microsoft.com/dotnet/standard/design-guidelines/using-standard-exception-types")
            .Fix("Catch the specific exception you can handle; the only sanctioned catch-all is the dispatcher's poll loop, where OutboxDispatcher logs and continues to the next poll.");

        // 10 — TAP: a Task-returning method accepts a CancellationToken so callers can cancel and flow the request down the chain.
        arch.Rule("async/accept-cancellation")
            .Enforce(arch.Types.InNamespace("Meridian.Interchange.*").Methods.Returning(typeof(Task), typeof(Task<>)).MustAcceptParameter(typeof(CancellationToken)))
            .Because("Accepting a CancellationToken lets a caller stop in-flight async work and flow that request on to the calls it makes, so a Task-returning method without one cannot take part in cooperative cancellation — https://learn.microsoft.com/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap")
            .Fix("Add a CancellationToken parameter and flow OutboxDispatcher's stoppingToken through the call chain, as ScopedDispatchRunner and OutboxProcessor already do.");

        // 11 — Architectural principles (persistence ignorance): a persisted type carries no ORM mapping attribute; validation DataAnnotations are untouched.
        arch.Rule("persistence/no-mapping-attributes")
            .Enforce(arch.Types.InNamespace("Meridian.Interchange.*")
                .MustNotBeAttributedWith(typeof(TableAttribute), typeof(ComplexTypeAttribute)))
            .Because("A persistence-specific attribute such as [Table] or [ComplexType] couples a persisted type to one data-access technology, so the same model can no longer be stored another way or moved to a new store; keep it ignorant of how it is persisted — https://learn.microsoft.com/dotnet/architecture/modern-web-apps-azure/architectural-principles")
            .Fix("Keep the persisted type a POCO and map it from the persistence layer with fluent configuration (EF Core's IEntityTypeConfiguration, a Dapper column list) instead of attributes on the type.");

        // 12 — CQRS reads (ViewModels/DTOs made for the consumer): a persisted entity must not surface on the public partner contract; hand partners a DTO.
        arch.Rule("contracts/no-entity-exposure")
            .Enforce(arch.Types.InNamespace("Meridian.Interchange.*")
                .Except(arch.Namespace("Meridian.Interchange.Outbox.*"))
                .MustNotExpose(typeof(OutboxMessage)))
            .Because("Exposing a persisted entity on a public signature couples partner-facing code to the storage model, so a change to how a message is persisted reshapes the partner contract; hand partners a DTO made for the wire instead — https://learn.microsoft.com/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/cqrs-microservice-reads")
            .Fix("Map the message to a PartnerEnvelope at the OutboxProcessor boundary and expose that; keep OutboxMessage inside the Outbox module.");
    }
}