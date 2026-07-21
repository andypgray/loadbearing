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
///     the provider outside that root, the hosted service scopes its own work, and Task-returning
///     methods carry the Async suffix — with the one legacy manifest corner that still blocks
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
    }
}