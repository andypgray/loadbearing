using MyApp.Legacy.Billing;

namespace Zphil.LoadBearing.MyAppRenderSpec;

/// <summary>
///     The render fixture spec: a module map (Domain/Web layers), two Enforce laws that hold on the
///     MyApp fixture, one Migrate rule (so the rendered root block carries a <c>### Migrations</c>
///     counter-prior paragraph — no <c>.Baseline</c> needed, since render reads no baseline in v1), and
///     one scope of each posture: a quarantine over <c>MyApp.Legacy.Billing</c> and a caution over
///     <c>RetryPolicy</c> in the Domain project. Rendering it against the MyApp solution produces the root
///     block plus a scope card in the billing directory and a caution card in the domain one — the render
///     e2e acceptance surface. Web carries a purpose and no anchored rule, so its module-map row carries the
///     sentence and no layer card is placed for it.
/// </summary>
public sealed class MyAppRenderSpec : IArchitectureSpec
{
    /// <inheritdoc />
    public void Define(Arch arch)
    {
        arch.Layer("Domain", "MyApp.Domain.*");
        arch.Layer("Web", "MyApp.Web.*").Purpose("The HTTP surface: controllers and the views they serve.");

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
            .Quarantine(arch.Namespace("MyApp.Legacy.Billing.*"))
            .BoundaryOnlyVia(typeof(IBillingFacade), typeof(BillingFacade))
            .Dragons("Banker's rounding happens at line-item level, NOT invoice level. " +
                     "Nightly reconciliation depends on this. Do not normalize.")
            .Because("Replacement scheduled (BillingV2, ADR-019); not worth stabilizing.");

        // The second scope posture, so one render covers both cards: a caution over a type in MyApp.Domain,
        // which has no layer card and no quarantine, so the card it places is the whole of that directory's
        // file. It also puts the two postures side by side in one root block — the "Quarantined scopes"
        // section stays containment-only, and the caution shows up only in the law diagram's not-drawn list.
        arch.Scope("domain/retry-budget")
            .Caution(arch.Types.Named("RetryPolicy"))
            .Dragons("RetryPolicy's broad catch is filtered on purpose: the `when` clause is what keeps it green " +
                     "under the unfiltered-catch rule, and it is the fixture's one sanctioned broad handler. Keep the " +
                     "filter; add cases beside it, never inside it.")
            .Because("The retry budget is the one place the domain sanctions a broad catch, and every caller relies on the filter.");
    }
}
