using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Zphil.LoadBearing.Packs.DotNet;

namespace Zphil.LoadBearing.Tests.Packs;

/// <summary>
///     The two halves of the parity oracle: the same nine canonical .NET rules, declared once through
///     the rule pack and once by hand exactly as an inline spec would write them — expression member
///     anchors and all. The arguments are Meridian.Interchange's, so what these two specs prove at unit
///     speed is what the example's committed <c>AGENTS.md</c> has to keep saying byte for byte.
/// </summary>
internal sealed class PackedNineSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        Selection host = arch.Namespace("Meridian.Interchange.Host.*");

        DotNetGuidance.ReuseHttpClient(arch, arch.Types, host, PackPosture.Enforce,
            "Take a typed or named client from IHttpClientFactory; see how CarrierClient receives its HttpClient.");

        DotNetGuidance.NoServiceLocator(arch, arch.Types, host, PackPosture.Enforce);

        DotNetGuidance.NoBuildServiceProvider(arch, arch.Types, PackPosture.Enforce);

        DotNetGuidance.NoSyncOverAsync(arch, arch.Types,
            PackPosture.Migrate("A legacy manifest gateway exposes only synchronous calls, so the adapter blocks on async work."),
            "Await the call and make the method async; the legacy corner is grandfathered until the SDK exposes async.");

        DotNetGuidance.NoCaptiveDependencies(arch, arch.Registered(Lifetime.Singleton), PackPosture.Enforce,
            "Resolve the scoped or transient service per unit of work inside an IServiceScopeFactory scope, as ScopedDispatchRunner does; take only singleton-safe dependencies in the constructor.");

        DotNetGuidance.AsyncSuffix(arch, arch.Types.InNamespace("Meridian.Interchange.*"), PackPosture.Enforce);

        DotNetGuidance.NoGeneralCatch(arch, arch.Types.InNamespace("Meridian.Interchange.*"),
            arch.Types.DerivedFrom<BackgroundService>(), PackPosture.Enforce,
            "Catch the specific exception you can handle; the only sanctioned catch-all is the dispatcher's poll loop, where OutboxDispatcher logs and continues to the next poll.");

        DotNetGuidance.AcceptCancellation(arch, arch.Types.InNamespace("Meridian.Interchange.*"), PackPosture.Enforce,
            "Add a CancellationToken parameter and flow OutboxDispatcher's stoppingToken through the call chain, as ScopedDispatchRunner and OutboxProcessor already do.");

        DotNetGuidance.NoMappingAttributes(arch, arch.Types.InNamespace("Meridian.Interchange.*"), PackPosture.Enforce);
    }
}

/// <summary>
///     The hand-written twin of <see cref="PackedNineSpec" /> — the nine rules as an ordinary spec
///     writes them, with the expression member anchors a spec outside the governed cone is free to use.
///     Nothing here may be edited to chase parity: when this and the packed half disagree, the pack is
///     what moved.
/// </summary>
internal sealed class InlineNineSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        Selection host = arch.Namespace("Meridian.Interchange.Host.*");

        arch.Rule("http/reuse-httpclient")
            .Enforce(arch.Types.Except(host).MustNotConstruct(typeof(HttpClient)))
            .Because("A new HttpClient per call exhausts sockets under load; IHttpClientFactory pools handlers — https://learn.microsoft.com/dotnet/fundamentals/networking/http/httpclient-guidelines")
            .Fix("Take a typed or named client from IHttpClientFactory; see how CarrierClient receives its HttpClient.");

        arch.Rule("di/no-service-locator")
            .Enforce(arch.Types.Except(host).MustNotUse(
                arch.Member<IServiceProvider>(sp => sp.GetService(typeof(object))),
                arch.Member(typeof(ServiceProviderServiceExtensions), nameof(ServiceProviderServiceExtensions.GetService)),
                arch.Member(typeof(ServiceProviderServiceExtensions), nameof(ServiceProviderServiceExtensions.GetRequiredService))))
            .Because("Resolving services from IServiceProvider at call sites hides a type's real dependencies; declare them as constructor parameters — https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines")
            .Fix("Take the dependency in the constructor; the composition root and its scope seam are the only sanctioned resolve sites.");

        arch.Rule("di/no-buildserviceprovider")
            .Enforce(arch.Types.MustNotUse(
                arch.Member(typeof(ServiceCollectionContainerBuilderExtensions), nameof(ServiceCollectionContainerBuilderExtensions.BuildServiceProvider))))
            .Because("Calling BuildServiceProvider while configuring services builds a second container with its own singletons — a duplicate-instance trap — https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines")
            .Fix("Register the dependency and let the host build the provider once; inject what you need.");

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

        arch.Rule("di/no-captive-dependencies")
            .Enforce(arch.Registered(Lifetime.Singleton).MustNotInject(
                arch.Registered(Lifetime.Scoped),
                arch.Registered(Lifetime.Transient)))
            .Because("A singleton is created once and holds every dependency it injects for the whole process, so a scoped or transient service injected into it is captured past its lifetime and shared across all callers — https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines")
            .Fix("Resolve the scoped or transient service per unit of work inside an IServiceScopeFactory scope, as ScopedDispatchRunner does; take only singleton-safe dependencies in the constructor.");

        arch.Rule("naming/async-suffix")
            .Enforce(arch.Types.InNamespace("Meridian.Interchange.*").Methods.Returning(typeof(Task), typeof(Task<>)).MustHaveSuffix("Async"))
            .Because("Task-returning methods carry the Async suffix so callers see at the call site that a method must be awaited — https://learn.microsoft.com/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap")
            .Fix("Rename the method to end in Async.");

        arch.Rule("exceptions/no-general-catch")
            .Enforce(arch.Types.InNamespace("Meridian.Interchange.*").Except(arch.Types.DerivedFrom<BackgroundService>())
                .MustNotCatch(typeof(Exception)))
            .Because("Catching base Exception outside a top-level handler swallows the faults you meant to see; the dispatcher's poll loop is that handler, so scope the catch-all there and let other code catch only the specific types it can handle — https://learn.microsoft.com/dotnet/standard/design-guidelines/using-standard-exception-types")
            .Fix("Catch the specific exception you can handle; the only sanctioned catch-all is the dispatcher's poll loop, where OutboxDispatcher logs and continues to the next poll.");

        arch.Rule("async/accept-cancellation")
            .Enforce(arch.Types.InNamespace("Meridian.Interchange.*").Methods.Returning(typeof(Task), typeof(Task<>)).MustAcceptParameter(typeof(CancellationToken)))
            .Because("Accepting a CancellationToken lets a caller stop in-flight async work and flow that request on to the calls it makes, so a Task-returning method without one cannot take part in cooperative cancellation — https://learn.microsoft.com/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap")
            .Fix("Add a CancellationToken parameter and flow OutboxDispatcher's stoppingToken through the call chain, as ScopedDispatchRunner and OutboxProcessor already do.");

        arch.Rule("persistence/no-mapping-attributes")
            .Enforce(arch.Types.InNamespace("Meridian.Interchange.*")
                .MustNotBeAttributedWith(typeof(TableAttribute), typeof(ComplexTypeAttribute)))
            .Because("A persistence-specific attribute such as [Table] or [ComplexType] couples a persisted type to one data-access technology, so the same model can no longer be stored another way or moved to a new store; keep it ignorant of how it is persisted — https://learn.microsoft.com/dotnet/architecture/modern-web-apps-azure/architectural-principles")
            .Fix("Keep the persisted type a POCO and map it from the persistence layer with fluent configuration (EF Core's IEntityTypeConfiguration, a Dapper column list) instead of attributes on the type.");
    }
}
