using System.Data;
using MyApp.Domain;
using MyApp.Legacy.Billing;
using MyApp.Web;

namespace Zphil.LoadBearing.MyAppViolatedSpec;

/// <summary>
///     A spec for the MyApp fixture whose single run exercises the whole report and JSON schema.
/// </summary>
/// <remarks>
///     Every violation kind, posture and baseline state the renderers can emit is carried by one rule
///     below, so a newly added kind needs a rule here or no end-to-end run covers it. Each rule states at
///     its own declaration what it proves and whether it reds.
/// </remarks>
public sealed class MyAppViolatedSpec : IArchitectureSpec
{
    /// <inheritdoc />
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

        // Quarantine (uncaptured): the explicit baseline path is deliberately never committed, so the
        // containment rule is uncaptured — InvoiceController's interior references to BillingCalculator
        // and RoundingMode are hard red. The divergent path (NOT the conventional default) keeps the
        // committed conventional baseline — which MyAppQuarantinedSpec grandfathers against — from capturing
        // this run. The tripwire skips without a --diff-base.
        arch.Scope("legacy/billing")
            .Quarantine(arch.Namespace("MyApp.Legacy.Billing.*"))
            .BoundaryOnlyVia(typeof(IBillingFacade), typeof(BillingFacade))
            .Baseline("arch/violated-quarantine-baseline.json")
            .Dragons("Banker's rounding happens at line-item level, NOT invoice level. Do not normalize.")
            .Because("Replacement scheduled; not worth stabilizing.");

        // Migrate (ratcheted, catch): omits .Baseline, so the conventional path arch/baselines/exceptions/
        // no-general-catch.json is uncommitted, leaving the rule uncaptured — ReportEndpoint's and
        // ReportPublisher's blanket `catch (Exception)`es are hard-red catch violations with the --init hint,
        // exercising the catch kind and the report/JSON schema's `catches` line (GRAMMAR §4.8).
        // MustNotCatch(typeof(Exception)) flags only the broad catch, never a narrower one, and judges nothing
        // about the filter or the rethrow — which is why both handlers red here and only one does two rules down.
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
        // InvoiceController's DataTable *reference* edge is grandfathered under data-access/no-inline-sql, but
        // baselines are per-rule, so the byte-identical exposure edge reds here — per-family capture, not per-type.
        arch.Rule("api/return-dtos")
            .Migrate(
                "Some controllers return a System.Data.DataTable straight from their public methods.",
                web.MustNotExpose(typeof(DataTable)))
            .Because("An infrastructure type on a presentation-layer public signature couples every caller to it; return a DTO or view model.")
            .Fix("Return a DTO instead of exposing System.Data.DataTable.");

        // Enforce (filter-aware catch ban): a union subject, so the sentence speaks in union voice — "The Web
        // or Domain layers must not …". ReportEndpoint's and ReportPublisher's blanket `catch (System.Exception)`
        // spell no `when` filter, so both are hard red at the SAME catch edges exceptions/no-general-catch reds,
        // under a different rule ID asking a different question. RetryPolicy catches the identical type in the
        // Domain layer behind a `when` filter: the edge is minted either way (a filter never suppresses it,
        // GRAMMAR §4.8), but only the unfiltered sites are evidence here, so RetryPolicy's is green and never
        // listed.
        arch.Rule("exceptions/no-unfiltered-catch")
            .Enforce(arch.AnyOf(web, domain).MustNotCatchUnfiltered(typeof(Exception)))
            .Because("An unfiltered broad catch swallows every failure alike; a `when` filter names the ones this handler actually expects.")
            .Fix("Add a `when` filter naming the exceptions you can handle, or catch those types directly.");

        // Enforce (rethrow-aware catch ban): the same union subject and the same banned type as the rule above,
        // asking the third question on the axis. ReportEndpoint's blanket catch returns -1 and is red here too;
        // ReportPublisher's identical unfiltered `catch (System.Exception)` ends in `throw;` and is GREEN, though
        // the rule above reds it — the differentiator, visible end-to-end in one report. RetryPolicy's filtered
        // catch stays green under both.
        arch.Rule("exceptions/no-swallowed-catch")
            .Enforce(arch.AnyOf(web, domain).MustNotSwallow(typeof(Exception)))
            .Because("A handler that catches everything and continues hands its caller a wrong answer that reads like a right one; rethrowing keeps the failure travelling.")
            .Fix("Rethrow after cleanup, translate to a domain exception, or add a `when` filter naming what you can handle.");

        // Enforce (throw ban): the ban polarity beside the strict allow-list above. OrderApproval's BCL throw
        // is red at the SAME throw edge exceptions/domain-throws-domain reds — two rules, two identities, one
        // edge. The domain's own OrderRuleViolation is deliberately NOT banned, and exact-FQN matching means
        // banning InvalidOperationException reaches neither it nor any other type.
        arch.Rule("exceptions/no-bare-bcl-throw")
            .Enforce(domain.MustNotThrow(typeof(InvalidOperationException)))
            .Because("InvalidOperationException tells a caller nothing it can dispatch on; the domain has its own exception for rule failures.")
            .Fix("Throw OrderRuleViolation instead of System.InvalidOperationException.");

        // Enforce (shape, residence): the spec's first shape-kind emitters — the three rules from here
        // down carry a verdict about the subject type itself, so a violation has no source/target/member
        // slots and its sites are the type's own declaration sites. OrderService is declared by
        // MyApp.Domain, so it reds with its declaration site as the evidence; InvoiceService is declared
        // by MyApp.Web and is green.
        arch.Rule("layering/services-in-web")
            .Enforce(arch.Types.WithSuffix("Service").MustResideInProject("MyApp.Web"))
            .Because("Services are wired by the web host; a service outside MyApp.Web escapes its registration sweep.")
            .Fix("Move the service into the MyApp.Web project.");

        // Enforce (shape, membership): the ungoverned-remainder rule — every MyApp.* type must belong to
        // a declared layer. All four MyApp.Legacy.Billing types red: Billing is quarantined under
        // legacy/billing above, but a quarantine is not a layer — the two rules read the same namespace
        // and answer different questions. The four-red form is deliberate; narrowing the subject to
        // shrink the report would hide exactly the remainder this rule exists to surface.
        arch.Rule("layering/no-ungoverned-types")
            .Enforce(arch.Namespace("MyApp.*").MustBelongTo(domain, web))
            .Because("A type in no declared layer is governed by no layer rule; the two layers are the covering set.")
            .Fix("Move the type into a declared layer's namespace, or declare its layer in this spec.");

        // Enforce (shape, registration): membership in the DI registration facts — the same set
        // arch.Registered() reads. InvoiceCreatedHandler and RefundProcessor implement IHandler<T> but
        // are never registered, so both red; ReportScheduler carries the Scheduler suffix AND is
        // registered as a singleton in ServiceWiring, so it is green — which makes the reds a statement
        // about registration, not about the verb.
        arch.Rule("di/handlers-registered")
            .Enforce(arch.AnyOf(
                    arch.Types.Implementing(typeof(IHandler<>)),
                    arch.Types.WithSuffix("Scheduler"))
                .MustBeRegistered())
            .Because("Handlers and schedulers are resolved from the container; one that is never registered fails at dispatch, not at startup.")
            .Fix("Register the type in ServiceWiring.Configure.");

        // Enforce (member-shape, mutability): the Domain layer's properties must declare no setter. Three
        // reds across two types, one per settable shape: Order.Reference and Order.Total are `{ get; set; }`,
        // and Money — a positional `readonly record struct` — carries the generated `{ get; init; }` Amount,
        // which reds too, because get-only is STRICT (GRAMMAR §5.7): an init-only setter is still a setter.
        // Order.Line's Name and Price are `{ get; }` and green, which is what makes the three reds a
        // statement about setters rather than about properties.
        arch.Rule("domain/values-immutable")
            .Enforce(domain.Properties.MustBeGetOnly())
            .Because("A value a caller can reassign after construction is not a value; the invariants the constructor checked stop holding the moment anyone writes to it.")
            .Fix("Take the value in the constructor and expose it `{ get; }`; hand back a new instance for a changed one.");

        // Enforce (member-shape, mutability + the first member shape adjective): the Web layer's STATIC
        // fields must be readonly. ReportBudget.RenderCount is the one red; DefaultFormat is static readonly
        // and MaxRows is const, both green — a const field satisfies the verb. The .ThatAreStatic() adjective
        // keeps ReportPublisher's writable instance _attempts out of the subject.
        arch.Rule("state/no-static-mutable")
            .Enforce(web.Fields.ThatAreStatic().MustBeReadonly())
            .Because("A writable static is state the whole process shares; the last write anywhere decides what the next request reads, and nothing in the signature says so.")
            .Fix("Make the field `readonly` or `const`, or move the state onto an instance whose lifetime the caller controls.");
    }
}
