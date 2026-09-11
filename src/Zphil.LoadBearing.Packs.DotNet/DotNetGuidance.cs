using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Zphil.LoadBearing.Packs.DotNet;

/// <summary>
///     Canonical .NET guidance as a rule pack: project-independent rules, one static method each,
///     declared on the caller's <see cref="Arch" />. A pack is an ordinary class library — there is no
///     plugin host and no discovery, so a rule lands only where a spec calls for it, and opting out
///     means not making the call.
/// </summary>
/// <remarks>
///     <para>
///         The pack owns each rule's <c>Because</c>, because the reason a rule exists is the same
///         everywhere. The consumer owns the posture and may override the <c>Fix</c>, because
///         remediation names local types. A <c>Fix</c> override is a parameter rather than a trailer:
///         every method returns <c>void</c>, so exactly one <c>Because</c> and one <c>Fix</c> reach the
///         rule and a second trailer is uncompilable rather than a validation error.
///     </para>
///     <para>
///         Method names are the rule-name half of the ID, PascalCased with the area dropped
///         (<c>http/reuse-httpclient</c> → <see cref="ReuseHttpClient" />), so the mapping needs no table.
///     </para>
///     <para>
///         Anchor doctrine: every member anchor is written <c>arch.Member(typeof(X), nameof(X.M))</c>,
///         never the expression form. This pack ships inside a codebase it governs, and an expression
///         anchor is real syntax — it would mint a use edge attributed to this type and make the pack a
///         violator of its own rules. <c>nameof</c> operands mint nothing. The two forms reify
///         identically, so the doctrine costs nothing.
///     </para>
/// </remarks>
public static class DotNetGuidance
{
    /// <summary>
    ///     <c>http/reuse-httpclient</c> — nothing outside <paramref name="compositionRoot" /> constructs
    ///     an <see cref="HttpClient" />.
    /// </summary>
    /// <param name="arch">The spec's stage-machine entry point.</param>
    /// <param name="subject">The types the rule governs.</param>
    /// <param name="compositionRoot">The wiring seam that is allowed to construct clients.</param>
    /// <param name="posture">Enforce, or Migrate with the project's counter-prior prose.</param>
    /// <param name="fix">A project-specific remediation hint, replacing the pack's generic one.</param>
    public static void ReuseHttpClient(
        Arch arch, Selection subject, Selection compositionRoot, PackPosture posture, string? fix = null)
    {
        IRuleBuilder rule = arch.Rule("http/reuse-httpclient");
        Constraint constraint = subject.Except(compositionRoot).MustNotConstruct(typeof(HttpClient));

        Declare(rule, constraint, posture,
            "A new HttpClient per call exhausts sockets under load; IHttpClientFactory pools handlers — https://learn.microsoft.com/dotnet/fundamentals/networking/http/httpclient-guidelines",
            "Take a typed or named client from IHttpClientFactory rather than constructing one.",
            fix);
    }

    /// <summary>
    ///     <c>di/no-service-locator</c> — nothing outside <paramref name="resolveSeam" /> resolves a
    ///     service from an <see cref="IServiceProvider" />.
    /// </summary>
    /// <param name="arch">The spec's stage-machine entry point.</param>
    /// <param name="subject">The types the rule governs.</param>
    /// <param name="resolveSeam">The sanctioned resolve sites — typically the composition root.</param>
    /// <param name="posture">Enforce, or Migrate with the project's counter-prior prose.</param>
    /// <param name="fix">A project-specific remediation hint, replacing the pack's generic one.</param>
    public static void NoServiceLocator(
        Arch arch, Selection subject, Selection resolveSeam, PackPosture posture, string? fix = null)
    {
        IRuleBuilder rule = arch.Rule("di/no-service-locator");
        Constraint constraint = subject.Except(resolveSeam).MustNotUse(
            arch.Member(typeof(IServiceProvider), nameof(IServiceProvider.GetService)),
            arch.Member(typeof(ServiceProviderServiceExtensions), nameof(ServiceProviderServiceExtensions.GetService)),
            arch.Member(typeof(ServiceProviderServiceExtensions), nameof(ServiceProviderServiceExtensions.GetRequiredService)));

        Declare(rule, constraint, posture,
            "Resolving services from IServiceProvider at call sites hides a type's real dependencies; declare them as constructor parameters — https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines",
            "Take the dependency in the constructor; the composition root and its scope seam are the only sanctioned resolve sites.",
            fix);
    }

    /// <summary>
    ///     <c>di/no-buildserviceprovider</c> — nothing builds a second container while configuring
    ///     services.
    /// </summary>
    /// <param name="arch">The spec's stage-machine entry point.</param>
    /// <param name="subject">The types the rule governs.</param>
    /// <param name="posture">Enforce, or Migrate with the project's counter-prior prose.</param>
    /// <param name="fix">A project-specific remediation hint, replacing the pack's generic one.</param>
    public static void NoBuildServiceProvider(
        Arch arch, Selection subject, PackPosture posture, string? fix = null)
    {
        IRuleBuilder rule = arch.Rule("di/no-buildserviceprovider");
        Constraint constraint = subject.MustNotUse(
            arch.Member(typeof(ServiceCollectionContainerBuilderExtensions), nameof(ServiceCollectionContainerBuilderExtensions.BuildServiceProvider)));

        Declare(rule, constraint, posture,
            "Calling BuildServiceProvider while configuring services builds a second container with its own singletons — a duplicate-instance trap — https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines",
            "Register the dependency and let the host build the provider once; inject what you need.",
            fix);
    }

    /// <summary>
    ///     <c>async/no-sync-over-async</c> — nothing blocks on a <see cref="Task" /> via
    ///     <c>Wait</c>/<c>Result</c>/<c>GetResult</c>.
    /// </summary>
    /// <param name="arch">The spec's stage-machine entry point.</param>
    /// <param name="subject">The types the rule governs.</param>
    /// <param name="posture">Enforce, or Migrate with the project's counter-prior prose.</param>
    /// <param name="fix">A project-specific remediation hint, replacing the pack's generic one.</param>
    public static void NoSyncOverAsync(
        Arch arch, Selection subject, PackPosture posture, string? fix = null)
    {
        IRuleBuilder rule = arch.Rule("async/no-sync-over-async");
        Constraint constraint = subject.MustNotUse(
            arch.Member(typeof(Task), nameof(Task.Wait)),
            arch.Member(typeof(Task<>), nameof(Task<>.Result)),
            arch.Member(typeof(Task), nameof(Task.GetAwaiter)),
            arch.Member(typeof(Task<>), nameof(Task<>.GetAwaiter)),
            arch.Member(typeof(TaskAwaiter), nameof(TaskAwaiter.GetResult)),
            arch.Member(typeof(TaskAwaiter<>), nameof(TaskAwaiter<>.GetResult)));

        Declare(rule, constraint, posture,
            "Blocking on a Task (.Result/.Wait/.GetResult) ties up a thread and can deadlock in a captured context; await instead — https://learn.microsoft.com/dotnet/csharp/asynchronous-programming/async-scenarios",
            "Await the call and make the method async.",
            fix);
    }

    /// <summary>
    ///     <c>di/no-captive-dependencies</c> — the singletons named by <paramref name="subject" /> inject
    ///     nothing scoped or transient.
    /// </summary>
    /// <param name="arch">The spec's stage-machine entry point.</param>
    /// <param name="subject">The singleton selection — typically <c>arch.Registered(Lifetime.Singleton)</c>.</param>
    /// <param name="posture">Enforce, or Migrate with the project's counter-prior prose.</param>
    /// <param name="fix">A project-specific remediation hint, replacing the pack's generic one.</param>
    public static void NoCaptiveDependencies(
        Arch arch, Selection subject, PackPosture posture, string? fix = null)
    {
        IRuleBuilder rule = arch.Rule("di/no-captive-dependencies");
        Constraint constraint = subject.MustNotInject(
            arch.Registered(Lifetime.Scoped),
            arch.Registered(Lifetime.Transient));

        Declare(rule, constraint, posture,
            "A singleton is created once and holds every dependency it injects for the whole process, so a scoped or transient service injected into it is captured past its lifetime and shared across all callers — https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines",
            "Resolve the scoped or transient service per unit of work inside an IServiceScopeFactory scope; take only singleton-safe dependencies in the constructor.",
            fix);
    }

    /// <summary>
    ///     <c>naming/async-suffix</c> — <see cref="Task" />-returning methods of
    ///     <paramref name="subject" /> carry the <c>Async</c> suffix.
    /// </summary>
    /// <param name="arch">The spec's stage-machine entry point.</param>
    /// <param name="subject">The types whose methods the rule governs; the pack applies the method projection.</param>
    /// <param name="posture">Enforce, or Migrate with the project's counter-prior prose.</param>
    /// <param name="fix">A project-specific remediation hint, replacing the pack's generic one.</param>
    public static void AsyncSuffix(
        Arch arch, Selection subject, PackPosture posture, string? fix = null)
    {
        IRuleBuilder rule = arch.Rule("naming/async-suffix");
        Constraint constraint = subject.Methods.Returning(typeof(Task), typeof(Task<>)).MustHaveSuffix("Async");

        Declare(rule, constraint, posture,
            "Task-returning methods carry the Async suffix so callers see at the call site that a method must be awaited — https://learn.microsoft.com/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap",
            "Rename the method to end in Async.",
            fix);
    }

    /// <summary>
    ///     <c>exceptions/no-general-catch</c> — nothing outside
    ///     <paramref name="topLevelHandler" /> catches base <see cref="Exception" />.
    /// </summary>
    /// <param name="arch">The spec's stage-machine entry point.</param>
    /// <param name="subject">The types the rule governs.</param>
    /// <param name="topLevelHandler">The one place a catch-all belongs.</param>
    /// <param name="posture">Enforce, or Migrate with the project's counter-prior prose.</param>
    /// <param name="fix">A project-specific remediation hint, replacing the pack's generic one.</param>
    public static void NoGeneralCatch(
        Arch arch, Selection subject, Selection topLevelHandler, PackPosture posture, string? fix = null)
    {
        IRuleBuilder rule = arch.Rule("exceptions/no-general-catch");
        Constraint constraint = subject.Except(topLevelHandler).MustNotCatch(typeof(Exception));

        Declare(rule, constraint, posture,
            "Catching base Exception outside a top-level handler swallows the faults you meant to see; the dispatcher's poll loop is that handler, so scope the catch-all there and let other code catch only the specific types it can handle — https://learn.microsoft.com/dotnet/standard/design-guidelines/using-standard-exception-types",
            "Catch the specific exception you can handle; leave the catch-all to the top-level handler.",
            fix);
    }

    /// <summary>
    ///     <c>async/accept-cancellation</c> — <see cref="Task" />-returning methods of
    ///     <paramref name="subject" /> accept a <see cref="CancellationToken" />.
    /// </summary>
    /// <param name="arch">The spec's stage-machine entry point.</param>
    /// <param name="subject">The types whose methods the rule governs; the pack applies the method projection.</param>
    /// <param name="posture">Enforce, or Migrate with the project's counter-prior prose.</param>
    /// <param name="fix">A project-specific remediation hint, replacing the pack's generic one.</param>
    public static void AcceptCancellation(
        Arch arch, Selection subject, PackPosture posture, string? fix = null)
    {
        IRuleBuilder rule = arch.Rule("async/accept-cancellation");
        Constraint constraint = subject.Methods.Returning(typeof(Task), typeof(Task<>)).MustAcceptParameter(typeof(CancellationToken));

        Declare(rule, constraint, posture,
            "Accepting a CancellationToken lets a caller stop in-flight async work and flow that request on to the calls it makes, so a Task-returning method without one cannot take part in cooperative cancellation — https://learn.microsoft.com/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap",
            "Add a CancellationToken parameter and flow the caller's token through the call chain.",
            fix);
    }

    /// <summary>
    ///     <c>persistence/no-mapping-attributes</c> — no type in <paramref name="subject" /> carries an
    ///     ORM mapping attribute.
    /// </summary>
    /// <param name="arch">The spec's stage-machine entry point.</param>
    /// <param name="subject">The types the rule governs.</param>
    /// <param name="posture">Enforce, or Migrate with the project's counter-prior prose.</param>
    /// <param name="fix">A project-specific remediation hint, replacing the pack's generic one.</param>
    public static void NoMappingAttributes(
        Arch arch, Selection subject, PackPosture posture, string? fix = null)
    {
        IRuleBuilder rule = arch.Rule("persistence/no-mapping-attributes");
        Constraint constraint = subject.MustNotBeAttributedWith(typeof(TableAttribute), typeof(ComplexTypeAttribute));

        Declare(rule, constraint, posture,
            "A persistence-specific attribute such as [Table] or [ComplexType] couples a persisted type to one data-access technology, so the same model can no longer be stored another way or moved to a new store; keep it ignorant of how it is persisted — https://learn.microsoft.com/dotnet/architecture/modern-web-apps-azure/architectural-principles",
            "Keep the persisted type a POCO and map it from the persistence layer with fluent configuration (EF Core's IEntityTypeConfiguration, a Dapper column list) instead of attributes on the type.",
            fix);
    }

    /// <summary>
    ///     Declares every rule in the pack at one posture, with no <c>Fix</c> overrides. A convenience for
    ///     proving the pack's full surface in one call — no real spec wants all of them, so prefer naming
    ///     the ones you mean.
    /// </summary>
    /// <param name="arch">The spec's stage-machine entry point.</param>
    /// <param name="subject">The types the whole-surface rules govern.</param>
    /// <param name="compositionRoot">The wiring seam exempted from client construction and service resolution.</param>
    /// <param name="singletons">The singleton selection <c>di/no-captive-dependencies</c> governs.</param>
    /// <param name="topLevelHandler">The one place a catch-all belongs.</param>
    /// <param name="posture">Enforce, or Migrate with the project's counter-prior prose.</param>
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
    // so each arm completes its own; exactly one Because and one Fix reach the rule either way. Note
    // arch.Rule(id) is deliberately NOT called here — it captures caller info, and one call site would
    // give all nine rules the same file:line.
    private static void Declare(
        IRuleBuilder rule,
        Constraint constraint,
        PackPosture posture,
        string because,
        string defaultFix,
        string? fix)
    {
        if (posture.Posture == Posture.Migrate)
            rule.Migrate(from: posture.From!, to: constraint).Because(because).Fix(fix ?? defaultFix);
        else
            rule.Enforce(constraint).Because(because).Fix(fix ?? defaultFix);
    }
}
