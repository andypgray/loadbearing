using MyApp.Legacy.Billing;

namespace Zphil.LoadBearing.MyAppRenderSpec;

/// <summary>
///     The render fixture spec: a module map (Domain/Web layers), two Enforce laws that hold on the
///     MyApp fixture, one Migrate rule (so the rendered root block carries a <c>### Migrations</c>
///     counter-prior paragraph — no <c>.Baseline</c> needed, since render reads no baseline in v1), and
///     one frozen scope over <c>MyApp.Legacy.Billing</c>. Rendering it against the MyApp solution
///     produces the root block plus a scope card placed in the billing directory — the render e2e
///     acceptance surface.
/// </summary>
public sealed class MyAppRenderSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Layer("Domain", "MyApp.Domain.*");
        arch.Layer("Web", "MyApp.Web.*");

        arch.Rule("layering/billing-independent")
            .Enforce(arch.Namespace("MyApp.Legacy.Billing.*").MustNotReference(arch.Namespace("MyApp.Web.*")))
            .Because("Billing must not reach up into the web layer.");

        arch.Rule("naming/interfaces")
            .Enforce(arch.Types.OfKind(TypeKind.Interface).InNamespace("MyApp.*").MustHavePrefix("I"))
            .Because("House naming convention; agents grep by I-prefix.");

        arch.Rule("data-access/no-inline-sql")
            .Migrate(
                "Controllers build DataTables inline (legacy Active Record style).",
                arch.Namespace("MyApp.Web.*").WithSuffix("Controller").MustNotReference(arch.Namespace("System.Data.*")))
            .Because("Repository pattern for testability.");

        arch.Scope("legacy/billing")
            .Freeze(arch.Namespace("MyApp.Legacy.Billing.*"))
            .BoundaryOnlyVia(typeof(IBillingFacade), typeof(BillingFacade))
            .Dragons("Banker's rounding happens at line-item level, NOT invoice level. " +
                     "Nightly reconciliation depends on this. Do not normalize.")
            .Because("Replacement scheduled (BillingV2, ADR-019); not worth stabilizing.");
    }
}