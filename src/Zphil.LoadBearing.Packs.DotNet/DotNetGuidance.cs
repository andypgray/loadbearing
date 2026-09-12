using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Zphil.LoadBearing.Packs.DotNet;

/// <summary>
///     Canonical .NET guidance as a rule pack: nine project-independent rules, one static method each,
///     declared on the <see cref="Arch" /> your spec is handed. A pack is an ordinary class library
///     with no plugin host and no discovery, so a rule lands only where a spec calls for it, and
///     opting out of one means not making the call. Taking a rule from here and also writing it
///     yourself is a duplicate rule ID, which is reported when the spec is loaded.
/// </summary>
/// <remarks>
///     <para>
///         The pack owns each rule's reason and the canonical page it cites, because those are the
///         same in every codebase and you can override neither. You own the posture, the selections
///         the rule governs, and the remediation hint: pass a replacement as the <c>fix</c> argument,
///         since these methods return nothing and leave no <c>.Fix(...)</c> to chain.
///     </para>
///     <para>
///         A method's name is the rule-name half of its ID, PascalCased with the area dropped
///         (<c>http/reuse-httpclient</c> is <see cref="ReuseHttpClient" />), so the mapping needs no
///         table.
///     </para>
/// </remarks>
// Anchor doctrine: every member anchor here is written arch.Member(typeof(X), nameof(X.M)), never
// the expression form. This pack ships inside a codebase it governs, and an expression anchor is
// real syntax — it would mint a use edge attributed to this type and make the pack a violator of
// its own rules. nameof operands mint nothing, and the two forms reify identically (GRAMMAR §13),
// so the doctrine costs nothing.
public static class DotNetGuidance
{
    // The two pages more than one rule rests on, written once so a moved page cannot be corrected on some
    // rules and missed on the others. The five pages a single rule cites stay at their own call site.
    private const string DependencyInjectionGuidelines =
        "https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines";

    private const string TaskAsyncPattern =
        "https://learn.microsoft.com/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap";

    /// <summary>
    ///     Declares <c>http/reuse-httpclient</c>: nothing in <paramref name="subject" /> outside
    ///     <paramref name="compositionRoot" /> constructs an <see cref="HttpClient" />. Every
    ///     <c>new HttpClient(...)</c> elsewhere is a violation; take a typed or named client from
    ///     <c>IHttpClientFactory</c> instead.
    /// </summary>
    /// <param name="arch">The <see cref="Arch" /> the spec is declaring on.</param>
    /// <param name="subject">The types the rule governs.</param>
    /// <param name="compositionRoot">The wiring seam that is allowed to construct clients.</param>
    /// <param name="posture">
    ///     <c>PackPosture.Enforce</c>, or <c>PackPosture.Migrate</c> with a line saying what the code
    ///     does today.
    /// </param>
    /// <param name="fix">What to do instead, in one line, replacing the pack's generic hint. Optional.</param>
    public static void ReuseHttpClient(
        Arch arch, Selection subject, Selection compositionRoot, PackPosture posture, string? fix = null)
    {
        IRuleBuilder rule = arch.Rule("http/reuse-httpclient");
        Constraint constraint = subject.Except(compositionRoot).MustNotConstruct(typeof(HttpClient));

        Declare(rule, constraint, posture,
            "A new HttpClient per call exhausts sockets under load; IHttpClientFactory pools handlers.",
            "https://learn.microsoft.com/dotnet/fundamentals/networking/http/httpclient-guidelines",
            "Take a typed or named client from IHttpClientFactory rather than constructing one.",
            fix);
    }

    /// <summary>
    ///     Declares <c>di/no-service-locator</c>: nothing in <paramref name="subject" /> outside
    ///     <paramref name="resolveSeam" /> calls <c>IServiceProvider.GetService</c> or the
    ///     <c>GetService</c> and <c>GetRequiredService</c> extension methods on it. Declare the dependency
    ///     as a constructor parameter instead.
    /// </summary>
    /// <param name="arch">The <see cref="Arch" /> the spec is declaring on.</param>
    /// <param name="subject">The types the rule governs.</param>
    /// <param name="resolveSeam">The sanctioned resolve sites — typically the composition root.</param>
    /// <param name="posture">
    ///     <c>PackPosture.Enforce</c>, or <c>PackPosture.Migrate</c> with a line saying what the code
    ///     does today.
    /// </param>
    /// <param name="fix">What to do instead, in one line, replacing the pack's generic hint. Optional.</param>
    public static void NoServiceLocator(
        Arch arch, Selection subject, Selection resolveSeam, PackPosture posture, string? fix = null)
    {
        IRuleBuilder rule = arch.Rule("di/no-service-locator");
        Constraint constraint = subject.Except(resolveSeam).MustNotUse(
            arch.Member(typeof(IServiceProvider), nameof(IServiceProvider.GetService)),
            arch.Member(typeof(ServiceProviderServiceExtensions), nameof(ServiceProviderServiceExtensions.GetService)),
            arch.Member(typeof(ServiceProviderServiceExtensions), nameof(ServiceProviderServiceExtensions.GetRequiredService)));

        Declare(rule, constraint, posture,
            "Resolving services from IServiceProvider at call sites hides a type's real dependencies; declare them as constructor parameters.",
            DependencyInjectionGuidelines,
            "Take the dependency in the constructor; the composition root and its scope seam are the only sanctioned resolve sites.",
            fix);
    }

    /// <summary>
    ///     Declares <c>di/no-buildserviceprovider</c>: nothing in <paramref name="subject" /> calls
    ///     <c>BuildServiceProvider</c>, which builds a second container with its own singletons while
    ///     services are still being configured.
    /// </summary>
    /// <param name="arch">The <see cref="Arch" /> the spec is declaring on.</param>
    /// <param name="subject">The types the rule governs.</param>
    /// <param name="posture">
    ///     <c>PackPosture.Enforce</c>, or <c>PackPosture.Migrate</c> with a line saying what the code
    ///     does today.
    /// </param>
    /// <param name="fix">What to do instead, in one line, replacing the pack's generic hint. Optional.</param>
    public static void NoBuildServiceProvider(
        Arch arch, Selection subject, PackPosture posture, string? fix = null)
    {
        IRuleBuilder rule = arch.Rule("di/no-buildserviceprovider");
        Constraint constraint = subject.MustNotUse(
            arch.Member(typeof(ServiceCollectionContainerBuilderExtensions), nameof(ServiceCollectionContainerBuilderExtensions.BuildServiceProvider)));

        Declare(rule, constraint, posture,
            "Calling BuildServiceProvider while configuring services builds a second container with its own singletons, a duplicate-instance trap.",
            DependencyInjectionGuidelines,
            "Register the dependency and let the host build the provider once; inject what you need.",
            fix);
    }

    /// <summary>
    ///     Declares <c>async/no-sync-over-async</c>: nothing in <paramref name="subject" /> blocks on a
    ///     <see cref="Task" /> or a <see cref="ValueTask" />. The banned members are <c>Task.Wait</c>,
    ///     <c>Result</c> on <c>Task&lt;T&gt;</c> and <c>ValueTask&lt;T&gt;</c>, and <c>GetResult</c> on
    ///     the four task awaiters. Await the call and make the method async instead.
    /// </summary>
    /// <param name="arch">The <see cref="Arch" /> the spec is declaring on.</param>
    /// <param name="subject">The types the rule governs.</param>
    /// <param name="posture">
    ///     <c>PackPosture.Enforce</c>, or <c>PackPosture.Migrate</c> with a line saying what the code
    ///     does today.
    /// </param>
    /// <param name="fix">What to do instead, in one line, replacing the pack's generic hint. Optional.</param>
    public static void NoSyncOverAsync(
        Arch arch, Selection subject, PackPosture posture, string? fix = null)
    {
        IRuleBuilder rule = arch.Rule("async/no-sync-over-async");
        (Member first, Member[] more) = BlockingWaitAnchors(arch);
        Constraint constraint = subject.MustNotUse(first, more);

        Declare(rule, constraint, posture,
            "Blocking on a Task or ValueTask (.Result/.Wait/.GetResult) ties up a thread and can deadlock in a captured context; await instead.",
            "https://learn.microsoft.com/dotnet/csharp/asynchronous-programming/async-scenarios",
            "Await the call and make the method async.",
            fix);
    }

    /// <summary>
    ///     The members <c>async/no-sync-over-async</c> bans, for a spec that writes its own blocking-wait
    ///     rule — its own ID, its own reason, its own exempt seam — and still wants one definition of what
    ///     blocking on a task is. The set is <c>Task.Wait</c>, <c>Result</c> on <c>Task&lt;T&gt;</c> and
    ///     <c>ValueTask&lt;T&gt;</c>, and <c>GetResult</c> on the four task awaiters. Destructure it and
    ///     spread it into the ban — <c>(Member blocking, Member[] more) = BlockingWaitAnchors(arch);</c>
    ///     then <c>subject.MustNotUse(blocking, more)</c> — the split being the shape <c>MustNotUse</c>
    ///     takes rather than two kinds of member. Call <see cref="NoSyncOverAsync" /> instead wherever the
    ///     pack's own reason and subject fit; a rule built on this set picks up whatever is added to it
    ///     later.
    /// </summary>
    /// <param name="arch">The <see cref="Arch" /> the spec is declaring on.</param>
    /// <returns>The first banned member, and the rest.</returns>
    public static (Member First, Member[] More) BlockingWaitAnchors(Arch arch)
    {
        return (arch.Member(typeof(Task), nameof(Task.Wait)), [
            arch.Member(typeof(Task<>), nameof(Task<>.Result)),
            arch.Member(typeof(ValueTask<>), nameof(ValueTask<>.Result)),
            arch.Member(typeof(TaskAwaiter), nameof(TaskAwaiter.GetResult)),
            arch.Member(typeof(TaskAwaiter<>), nameof(TaskAwaiter<>.GetResult)),
            arch.Member(typeof(ValueTaskAwaiter), nameof(ValueTaskAwaiter.GetResult)),
            arch.Member(typeof(ValueTaskAwaiter<>), nameof(ValueTaskAwaiter<>.GetResult))
        ]);
    }

    /// <summary>
    ///     Declares <c>di/no-captive-dependencies</c>: no type in <paramref name="subject" /> takes a
    ///     constructor parameter typed on a service the source registers as scoped or as transient. A
    ///     singleton holding one captures it past its lifetime and shares it across every caller.
    /// </summary>
    /// <param name="arch">The <see cref="Arch" /> the spec is declaring on.</param>
    /// <param name="subject">
    ///     The singletons the rule governs — typically <c>arch.Registered(Lifetime.Singleton)</c>.
    /// </param>
    /// <param name="posture">
    ///     <c>PackPosture.Enforce</c>, or <c>PackPosture.Migrate</c> with a line saying what the code
    ///     does today.
    /// </param>
    /// <param name="fix">What to do instead, in one line, replacing the pack's generic hint. Optional.</param>
    public static void NoCaptiveDependencies(
        Arch arch, Selection subject, PackPosture posture, string? fix = null)
    {
        IRuleBuilder rule = arch.Rule("di/no-captive-dependencies");
        Constraint constraint = subject.MustNotInject(
            arch.Registered(Lifetime.Scoped),
            arch.Registered(Lifetime.Transient));

        Declare(rule, constraint, posture,
            "A singleton is created once and holds every dependency it injects for the whole process, so a scoped or transient service injected into it is captured past its lifetime and shared across all callers.",
            DependencyInjectionGuidelines,
            "Resolve the scoped or transient service per unit of work inside an IServiceScopeFactory scope; take only singleton-safe dependencies in the constructor.",
            fix);
    }

    /// <summary>
    ///     Declares <c>naming/async-suffix</c>: every <see cref="Task" />- or
    ///     <see cref="ValueTask" />-returning method the types in <paramref name="subject" /> declare is
    ///     named with the <c>Async</c> suffix, so a caller sees at the call site that it must be awaited.
    /// </summary>
    /// <param name="arch">The <see cref="Arch" /> the spec is declaring on.</param>
    /// <param name="subject">
    ///     The types whose methods the rule governs; the pack narrows them to the ones no source generator
    ///     emitted and looks at their methods.
    /// </param>
    /// <param name="posture">
    ///     <c>PackPosture.Enforce</c>, or <c>PackPosture.Migrate</c> with a line saying what the code
    ///     does today.
    /// </param>
    /// <param name="fix">What to do instead, in one line, replacing the pack's generic hint. Optional.</param>
    public static void AsyncSuffix(
        Arch arch, Selection subject, PackPosture posture, string? fix = null)
    {
        IRuleBuilder rule = arch.Rule("naming/async-suffix");
        Constraint constraint = subject.Authored().Methods
            .Returning(typeof(Task), typeof(Task<>), typeof(ValueTask), typeof(ValueTask<>))
            .MustHaveSuffix("Async");

        Declare(rule, constraint, posture,
            "Task- and ValueTask-returning methods carry the Async suffix so callers see at the call site that a method must be awaited.",
            TaskAsyncPattern,
            "Rename the method to end in Async.",
            fix);
    }

    /// <summary>
    ///     Declares <c>exceptions/no-general-catch</c>: nothing in <paramref name="subject" /> outside
    ///     <paramref name="topLevelHandler" /> catches base <see cref="Exception" /> and swallows it. A
    ///     clause that filters with <c>when</c>, or that rethrows, is not a violation.
    /// </summary>
    /// <param name="arch">The <see cref="Arch" /> the spec is declaring on.</param>
    /// <param name="subject">The types the rule governs.</param>
    /// <param name="topLevelHandler">The one place a catch-all belongs.</param>
    /// <param name="posture">
    ///     <c>PackPosture.Enforce</c>, or <c>PackPosture.Migrate</c> with a line saying what the code
    ///     does today.
    /// </param>
    /// <param name="fix">What to do instead, in one line, replacing the pack's generic hint. Optional.</param>
    public static void NoGeneralCatch(
        Arch arch, Selection subject, Selection topLevelHandler, PackPosture posture, string? fix = null)
    {
        IRuleBuilder rule = arch.Rule("exceptions/no-general-catch");
        Constraint constraint = subject.Except(topLevelHandler).MustNotSwallow(typeof(Exception));

        Declare(rule, constraint, posture,
            "Catching base Exception outside a top-level handler swallows the faults you meant to see; scope the catch-all to that handler and let other code catch only the specific types it can handle.",
            "https://learn.microsoft.com/dotnet/standard/design-guidelines/using-standard-exception-types",
            "Catch the specific exception you can handle, filter with `when`, or rethrow after cleanup; leave the catch-all to the top-level handler.",
            fix);
    }

    /// <summary>
    ///     Declares <c>async/accept-cancellation</c>: every <see cref="Task" />- or
    ///     <see cref="ValueTask" />-returning method the types in <paramref name="subject" /> declare
    ///     accepts a <see cref="CancellationToken" /> parameter, so a caller can stop in-flight work and
    ///     flow its token on through the calls the method makes.
    /// </summary>
    /// <param name="arch">The <see cref="Arch" /> the spec is declaring on.</param>
    /// <param name="subject">
    ///     The types whose methods the rule governs; the pack narrows them to the ones no source generator
    ///     emitted and looks at their methods.
    /// </param>
    /// <param name="posture">
    ///     <c>PackPosture.Enforce</c>, or <c>PackPosture.Migrate</c> with a line saying what the code
    ///     does today.
    /// </param>
    /// <param name="fix">What to do instead, in one line, replacing the pack's generic hint. Optional.</param>
    public static void AcceptCancellation(
        Arch arch, Selection subject, PackPosture posture, string? fix = null)
    {
        IRuleBuilder rule = arch.Rule("async/accept-cancellation");
        Constraint constraint = subject.Authored().Methods
            .Returning(typeof(Task), typeof(Task<>), typeof(ValueTask), typeof(ValueTask<>))
            .MustAcceptParameter<CancellationToken>();

        Declare(rule, constraint, posture,
            "Accepting a CancellationToken lets a caller stop in-flight async work and flow that request on to the calls it makes, so a Task- or ValueTask-returning method without one cannot take part in cooperative cancellation.",
            TaskAsyncPattern,
            "Add a CancellationToken parameter and flow the caller's token through the call chain.",
            fix);
    }

    /// <summary>
    ///     Declares <c>persistence/no-mapping-attributes</c>: no type in <paramref name="subject" />
    ///     carries <c>[Table]</c> or <c>[ComplexType]</c>, so a persisted type stays ignorant of how it is
    ///     stored and can be mapped from the persistence layer instead.
    /// </summary>
    /// <param name="arch">The <see cref="Arch" /> the spec is declaring on.</param>
    /// <param name="subject">The types the rule governs.</param>
    /// <param name="posture">
    ///     <c>PackPosture.Enforce</c>, or <c>PackPosture.Migrate</c> with a line saying what the code
    ///     does today.
    /// </param>
    /// <param name="fix">What to do instead, in one line, replacing the pack's generic hint. Optional.</param>
    public static void NoMappingAttributes(
        Arch arch, Selection subject, PackPosture posture, string? fix = null)
    {
        IRuleBuilder rule = arch.Rule("persistence/no-mapping-attributes");
        Constraint constraint = subject.MustNotBeAttributedWith(typeof(TableAttribute), typeof(ComplexTypeAttribute));

        Declare(rule, constraint, posture,
            "A persistence-specific attribute such as [Table] or [ComplexType] couples a persisted type to one data-access technology, so the same model can no longer be stored another way or moved to a new store; keep it ignorant of how it is persisted.",
            "https://learn.microsoft.com/dotnet/architecture/modern-web-apps-azure/architectural-principles",
            "Keep the persisted type a POCO and map it from the persistence layer with fluent configuration (EF Core's IEntityTypeConfiguration, a Dapper column list) instead of attributes on the type.",
            fix);
    }

    /// <summary>
    ///     Declares all nine of the pack's rules at one posture, each with the pack's own remediation
    ///     hint. A convenience for exercising the whole pack in one call: no real spec wants all nine over
    ///     the same subject at the same posture, so name the ones you mean instead.
    /// </summary>
    /// <param name="arch">The <see cref="Arch" /> the spec is declaring on.</param>
    /// <param name="subject">The types the rules govern, except the captive-dependency rule.</param>
    /// <param name="compositionRoot">The wiring seam exempted from client construction and service resolution.</param>
    /// <param name="singletons">The singletons <c>di/no-captive-dependencies</c> governs.</param>
    /// <param name="topLevelHandler">The one place a catch-all belongs.</param>
    /// <param name="posture">
    ///     <c>PackPosture.Enforce</c>, or <c>PackPosture.Migrate</c> with a line saying what the code
    ///     does today.
    /// </param>
    public static void ApplyAll(
        Arch arch,
        Selection subject,
        Selection compositionRoot,
        Selection singletons,
        Selection topLevelHandler,
        PackPosture posture)
    {
        ReuseHttpClient(arch, subject, compositionRoot, posture);
        NoServiceLocator(arch, subject, compositionRoot, posture);
        NoBuildServiceProvider(arch, subject, posture);
        NoSyncOverAsync(arch, subject, posture);
        NoCaptiveDependencies(arch, singletons, posture);
        AsyncSuffix(arch, subject, posture);
        NoGeneralCatch(arch, subject, topLevelHandler, posture);
        AcceptCancellation(arch, subject, posture);
        NoMappingAttributes(arch, subject, posture);
    }

    // The one place a posture becomes a chain. IEnforceRule and IMigrateRule are unrelated interfaces,
    // so each arm completes its own; exactly one Because, one Citation and one Fix reach the rule either
    // way. Note arch.Rule(id) is deliberately NOT called here — it captures caller info, and one call site
    // would give all nine rules the same file:line.
    private static void Declare(
        IRuleBuilder rule,
        Constraint constraint,
        PackPosture posture,
        string because,
        string citation,
        string defaultFix,
        string? fix)
    {
        if (posture.Posture == Posture.Migrate)
            rule.Migrate(from: posture.From!, to: constraint).Because(because).Citation(citation).Fix(fix ?? defaultFix);
        else
            rule.Enforce(constraint).Because(because).Citation(citation).Fix(fix ?? defaultFix);
    }
}
