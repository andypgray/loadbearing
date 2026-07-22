using System.Data;
using MyApp.Domain;
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
///     failing empty-subject rule, a frozen billing scope whose containment is uncaptured (an explicit,
///     deliberately-uncommitted
///     baseline path — hard red) and whose tripwire skips without a <c>--diff-base</c>, a ratcheted Migrate
///     catch rule (uncaptured — ReportEndpoint's blanket <c>catch (Exception)</c> is red, exercising the
///     <c>catch</c> kind), a strict Enforce throw rule (the Domain layer must throw only its own
///     <c>OrderRuleViolation</c>, so OrderApproval's BCL throw is red, exercising the <c>throw</c> kind), and
///     an Enforce member-subject parameter rule (the Web layer's Task-returning methods must accept a
///     <c>CancellationToken</c>, so all three of HomeController's tokenless ones — <c>Save</c>, <c>Load</c>,
///     and <c>SaveAsync</c> — are red, reusing the <c>memberShape</c> kind and <c>subjectMember</c> field;
///     <c>SaveAsync</c> is green under <c>naming/async-suffix</c> but red here), and a ratcheted Migrate
///     exposure rule (uncaptured — both HomeController's and InvoiceController's public methods that return a
///     <c>System.Data.DataTable</c> surface it on their public signature, exercising the <c>expose</c> kind
///     (GRAMMAR §4.9); InvoiceController is grandfathered for its DataTable reference edge under
///     <c>data-access/no-inline-sql</c> but red here, because the exposure edge is a different baseline
///     identity — per-family identity, not per-type).
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

        // Migrate (ratcheted, catch): omits .Baseline, so the conventional path arch/baselines/exceptions/
        // no-general-catch.json is uncommitted, leaving the rule uncaptured — ReportEndpoint's blanket
        // `catch (Exception)` is a hard-red catch violation with the --init hint, exercising the catch kind
        // and the report/JSON schema's `catches` line (GRAMMAR §4.8). MustNotCatch(typeof(Exception)) flags
        // only the broad catch, never a narrower one.
        arch.Rule("exceptions/no-general-catch")
            .Migrate(
                "Some handlers wrap their work in a blanket catch and swallow every exception.",
                web.MustNotCatch(typeof(Exception)))
            .Because("A catch-all hides the failures you meant to handle; catch the specific exception instead.")
            .Fix("Catch the specific exception type you can handle, not System.Exception.");

        // Enforce (strict throw allow-list): the Domain layer may throw only its own OrderRuleViolation.
        // OrderApproval's `throw new InvalidOperationException(...)` is an unlisted BCL throw — hard red — while
        // its `throw new OrderRuleViolation(...)` is the sanctioned one (green). MustOnlyThrow constrains
        // external thrown types too (GRAMMAR §4.8), so the BCL throw is not exempt — the throw half of the
        // report/JSON schema (the throw kind, the `throws` line).
        arch.Rule("exceptions/domain-throws-domain")
            .Enforce(domain.MustOnlyThrow(arch.Type<OrderRuleViolation>()))
            .Because("Domain code signals rule failures with the domain's own exception, not a generic BCL type.")
            .Fix("Throw OrderRuleViolation (or another domain exception) instead of a BCL exception type.");

        // Enforce (member-shape): the Web layer's Task-returning methods must accept a CancellationToken.
        // Enforce is a hard red (no baseline, nothing to grandfather), so all THREE tokenless Task-returning
        // methods fail — Save (:43), Load (:48, Task<int> — the open-generic match) and SaveAsync (:53).
        // SaveAsync is green under naming/async-suffix (it carries the suffix) but red here: the two
        // member-subject rules are orthogonal, both keyed on the member DocId and driving the memberShape kind.
        arch.Rule("async/accept-cancellation")
            .Enforce(web.Methods.Returning(typeof(Task), typeof(Task<>)).MustAcceptParameter(typeof(CancellationToken)))
            .Because("A Task-returning method that ignores cancellation cannot be stopped once its caller has moved on.")
            .Fix("Add a CancellationToken parameter and flow it to the calls you await.");

        // Migrate (ratcheted, exposure): omits .Baseline, so the conventional path arch/baselines/api/
        // return-dtos.json is uncommitted, leaving the rule uncaptured — both HomeController's ExportOrders and
        // InvoiceController's ExportInvoices return a System.Data.DataTable straight from their public signature,
        // so both are hard red with the --init hint, exercising the expose kind (GRAMMAR §4.9). The whole point:
        // InvoiceController's DataTable *reference* edge is grandfathered under data-access/no-inline-sql, but the
        // *exposure* edge is a different baseline identity, so it reds here — per-family identity, not per-type.
        arch.Rule("api/return-dtos")
            .Migrate(
                "Some controllers return a System.Data.DataTable straight from their public methods.",
                web.MustNotExpose(typeof(DataTable)))
            .Because("An infrastructure type on a presentation-layer public signature couples every caller to it; return a DTO or view model.")
            .Fix("Return a DTO instead of exposing System.Data.DataTable.");
    }
}