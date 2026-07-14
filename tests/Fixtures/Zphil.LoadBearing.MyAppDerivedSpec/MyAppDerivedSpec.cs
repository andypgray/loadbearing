using MyApp.Legacy.Billing;
using MyApp.Web;

namespace Zphil.LoadBearing.MyAppDerivedSpec;

/// <summary>
///     The golden artifact of walking the <c>derive_spec</c> prompt against the MyApp fixture: the
///     post-curation spec a derive flow produces, exercising all three postures with evidence-assigned
///     roles. Enforce carries the two directions the survey showed already clean; Migrate carries the
///     three observed debts (2 layering edges, 2 inline-SQL edges, 1 handler-naming shape — the shape
///     entry matters: it pins a <c>subject</c> baseline line); Freeze contains the billing dragons with
///     2 grandfatherable interior references. Every baseline path is the conventional default, so on a
///     virgin estate (no <c>arch/</c>) <c>check</c> is red with 7 violations across 4 rules, one
///     <c>baseline --init</c> captures them all, and the re-check is exit 0 —
///     <c>DeriveFlowE2ETests</c> pins exactly that sequence.
/// </summary>
public sealed class MyAppDerivedSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        Layer domain = arch.Layer("Domain", "MyApp.Domain.*");
        Layer web = arch.Layer("Web", "MyApp.Web.*");

        // Survey evidence: zero observed Web -> Domain edges. Already law; Enforce costs nothing.
        arch.Rule("layering/web-no-domain")
            .Enforce(web.MustNotReference(domain))
            .Because("Web composes handlers and views; domain services stay behind abstractions.");

        // Survey evidence: zero observed Billing -> Web edges.
        arch.Rule("layering/billing-no-web")
            .Enforce(arch.Namespace("MyApp.Legacy.Billing.*").MustNotReference(web))
            .Because("Billing must not reach up into the web layer.");

        // Evidence: 2 edges (OrderService -> HomeController, OrderService -> WebTextExtensions).
        arch.Rule("layering/domain-independent")
            .Migrate(
                from: "OrderService renders through web helpers directly (legacy UI coupling in the domain).",
                to: domain.MustNotReference(web))
            .Because("Domain is UI-agnostic; transaction boundaries live in services.")
            .Fix("Define an abstraction in Domain and implement it in Web.");

        // Evidence: 2 edges (HomeController and InvoiceController both build System.Data results).
        arch.Rule("data-access/no-inline-sql")
            .Migrate(
                from: "Controllers build System.Data results inline (legacy Active Record style).",
                to: web.WithSuffix("Controller").MustNotReference(arch.Namespace("System.Data.*")))
            .Because("Repository pattern for testability — ADR-012.")
            .Fix("Inject the repository; see OrdersRepository for the pattern.");

        // Evidence: 1 nonconforming shape (RefundProcessor implements IHandler<T> without the suffix).
        arch.Rule("naming/handlers")
            .Migrate(
                from: "Early handlers predate the *Handler naming convention.",
                to: arch.Types.Implementing(typeof(IHandler<>)).MustHaveSuffix("Handler"))
            .Because("Handler discovery is convention-based (see HandlerRegistry).")
            .Fix("Rename the type to end in Handler; discovery picks it up by name.");

        // No target state: the boundary is the enforceable thing. 2 pre-existing interior references
        // (InvoiceController -> BillingCalculator, InvoiceController -> RoundingMode) grandfather at init;
        // HomeController -> IBillingFacade is the sanctioned surface and never reds.
        arch.Scope("legacy/billing")
            .Freeze(arch.Namespace("MyApp.Legacy.Billing.*"))
            .BoundaryOnlyVia(typeof(IBillingFacade), typeof(BillingFacade))
            .Dragons("Banker's rounding happens at line-item level, NOT invoice level. " +
                     "Nightly reconciliation depends on this. Do not normalize.")
            .Because("Replacement scheduled (BillingV2); not worth stabilizing.");
    }
}
