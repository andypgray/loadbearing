using MyApp.Legacy.Billing;
using MyApp.Web;

namespace Zphil.LoadBearing.MyAppViolatedSpec;

/// <summary>
///     A spec for the MyApp fixture whose one run exercises the whole report/JSON schema: a failing
///     Enforce rule (the acceptance rule — Domain must not reference Web, which OrderService breaks),
///     a passing rule, a ratcheted Migrate rule (its conventional baseline grandfathers InvoiceController's
///     DataTable but not HomeController's — so it fails red on the new site), a member-use Migrate rule
///     (uncaptured — both of HomeController's ambient-clock reads, <c>DateTime.Now</c> and
///     <c>DateTime.UtcNow</c>, are red), a member-subject Migrate rule (uncaptured — both of
///     HomeController's unsuffixed Task-returning methods, <c>Save</c> and <c>Load</c>, are red, exercising
///     the <c>memberShape</c> kind and the <c>subjectMember</c> field), a ratcheted Migrate construction rule
///     (uncaptured — the DI flagship, InvoiceService news up a handler instead of resolving it through
///     HandlerRegistry, exercising the <c>construction</c> kind), a ratcheted Migrate injection rule
///     (uncaptured — the captive-dependency flagship, the singleton ReportScheduler injects a scoped
///     <c>IOrderFeed</c> and a transient <c>IOrderFormatter</c>, exercising the <c>injection</c> kind), an
///     inert-target warning rule, a
///     failing empty-subject rule, and a frozen billing scope whose containment is uncaptured (an explicit,
///     deliberately-uncommitted
///     baseline path — hard red) and whose tripwire skips without a <c>--diff-base</c>.
/// </summary>
public sealed class MyAppViolatedSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        Layer domain = arch.Layer("Domain", "MyApp.Domain.*");
        Layer web = arch.Layer("Web", "MyApp.Web.*");

        // Fails: MyApp.Domain.OrderService references HomeController and WebTextExtensions.
        arch.Rule("layering/domain-independent")
            .Enforce(domain.MustNotReference(web))
            .Because("Domain is UI-agnostic; transaction boundaries live in services.")
            .Fix("Define an abstraction in Domain and implement it in Web.");

        // Passes: Billing never reaches up into the web layer.
        arch.Rule("layering/billing-independent")
            .Enforce(arch.Namespace("MyApp.Legacy.Billing.*").MustNotReference(arch.Namespace("MyApp.Web.*")))
            .Because("Billing must not reach up into the web layer.");

        // Migrate (ratcheted): omits .Baseline, so the conventional path arch/baselines/data-access/
        // no-inline-sql.json grandfathers InvoiceController's DataTable. HomeController's is new → red.
        arch.Rule("data-access/no-inline-sql")
            .Migrate(
                "Some controllers open database connections directly.",
                web.WithSuffix("Controller").MustNotReference(arch.Namespace("System.Data.*")))
            .Because("Repository pattern for testability.")
            .Fix("Inject the repository.");

        // Migrate (ratcheted, member-level): the conventional baseline path is uncommitted, so both of
        // HomeController's ambient-clock reads are red with the --init hint — the member-use half of the
        // report/JSON schema ('uses' lines, targetMember field).
        arch.Rule("time/inject-clock")
            .Migrate(
                "Code reads the ambient clock directly.",
                web.MustNotUse(
                    () => DateTime.Now,
                    () => DateTime.UtcNow))
            .Because("Wall-clock reads are untestable; inject IClock.")
            .Fix("Take IClock in the constructor; see OrderService for the pattern.");

        // Migrate (ratcheted, member-subject): the conventional baseline path arch/baselines/naming/
        // async-suffix.json is uncommitted, so both of HomeController's unsuffixed Task-returning methods
        // (Save returning Task, Load returning Task<int> — the open-generic match) are red — the
        // member-subject half of the report/JSON schema (the memberShape kind, the subjectMember field).
        arch.Rule("naming/async-suffix")
            .Migrate(
                "Some Task-returning methods lack the Async suffix.",
                web.Methods.Returning(typeof(Task), typeof(Task<>)).MustHaveSuffix("Async"))
            .Because("Async methods are discovered by their Async suffix.")
            .Fix("Rename the method to end in Async and update its callers.");

        // Migrate (ratcheted, construction): the conventional baseline path arch/baselines/di/
        // handlers-via-registry.json is uncommitted, so InvoiceService's direct `new` of a handler is a
        // hard-red construction violation with the --init hint — the construction half of the report/JSON
        // schema (the 'construction' kind, GRAMMAR §5.3). The registry is carved out of the subject by
        // .Except, so its own construction of a handler is exempt.
        arch.Rule("di/handlers-via-registry")
            .Migrate(
                "Some services construct handlers directly instead of resolving them through HandlerRegistry.",
                arch.Types.Except(arch.Type<HandlerRegistry>())
                    .MustNotConstruct(arch.Types.Implementing(typeof(IHandler<>))))
            .Because("Handlers are resolved through HandlerRegistry; direct construction bypasses discovery.")
            .Fix("Resolve the handler through HandlerRegistry instead of constructing it.");

        // Migrate (ratcheted, injection): the conventional baseline path arch/baselines/di/
        // no-captive-dependencies.json is uncommitted, so both of ReportScheduler's captive edges are hard red
        // with the --init hint — the injection half of the report/JSON schema (the injection kind, GRAMMAR §4.7).
        // ReportScheduler is the only singleton; it injects a scoped IOrderFeed and a transient IOrderFormatter.
        arch.Rule("di/no-captive-dependencies")
            .Migrate(
                "Some singletons capture shorter-lived services through constructor injection.",
                arch.Registered(Lifetime.Singleton)
                    .MustNotInject(arch.Registered(Lifetime.Scoped), arch.Registered(Lifetime.Transient)))
            .Because("A singleton that captures a scoped or transient service pins it to the whole process lifetime.")
            .Fix("Inject IServiceScopeFactory and resolve the dependency inside a scope.");

        // Inert warning: the target pattern matches nothing, so the rule can never fire.
        arch.Rule("layering/no-ghost")
            .Enforce(arch.Namespace("MyApp.Domain.*").MustNotReference(arch.Namespace("MyApp.Ghost.*")))
            .Because("Nothing should reference the (nonexistent) ghost layer.");

        // Fails: an empty subject selection fails loudly by default.
        arch.Rule("naming/nonexistent")
            .Enforce(arch.Namespace("MyApp.Nowhere.*").MustHaveSuffix("Service"))
            .Because("A subject that matches nothing must fail loudly.");

        // Freeze (uncaptured): the explicit baseline path is deliberately never committed, so the
        // containment rule is uncaptured — InvoiceController's interior references to BillingCalculator
        // and RoundingMode are hard red. The divergent path (NOT the conventional default) keeps the
        // committed conventional baseline — which MyAppFrozenSpec grandfathers against — from capturing
        // this run. The tripwire skips without a --diff-base.
        arch.Scope("legacy/billing")
            .Freeze(arch.Namespace("MyApp.Legacy.Billing.*"))
            .BoundaryOnlyVia(typeof(IBillingFacade), typeof(BillingFacade))
            .Baseline("arch/violated-freeze-baseline.json")
            .Dragons("Banker's rounding happens at line-item level, NOT invoice level. Do not normalize.")
            .Because("Replacement scheduled; not worth stabilizing.");
    }
}