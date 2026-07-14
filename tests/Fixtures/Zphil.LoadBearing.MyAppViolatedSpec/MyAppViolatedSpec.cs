using MyApp.Legacy.Billing;

namespace Zphil.LoadBearing.MyAppViolatedSpec;

/// <summary>
///     A spec for the MyApp fixture whose one run exercises the whole report/JSON schema: a failing
///     Enforce rule (the acceptance rule — Domain must not reference Web, which OrderService breaks),
///     a passing rule, a ratcheted Migrate rule (its conventional baseline grandfathers InvoiceController's
///     DataTable but not HomeController's — so it fails red on the new site), an inert-target warning
///     rule, a failing empty-subject rule, and a frozen billing scope whose containment is uncaptured
///     (an explicit, deliberately-uncommitted baseline path — hard red) and whose tripwire skips without
///     a <c>--diff-base</c>.
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