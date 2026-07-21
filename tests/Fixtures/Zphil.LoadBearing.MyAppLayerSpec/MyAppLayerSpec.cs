using MyApp.Legacy.Billing;

namespace Zphil.LoadBearing.MyAppLayerSpec;

/// <summary>
///     The layer-card render fixture: a Web layer and a Billing layer, each carrying one anchored
///     Enforce rule, plus the canonical billing freeze over the Billing layer. Rendered against the
///     MyApp solution it produces the root block, a Web local-rules card in <c>MyApp.Web/</c>, and —
///     in <c>MyApp.Legacy.Billing/</c> — a merged block whose Billing layer card precedes the
///     frozen-scope card (layer key before freeze key). That Billing directory is the
///     both-cards-in-one-directory acceptance surface; the frozen layer's desugared containment is
///     layer-anchored yet Freeze-posture, so it renders only as the freeze card and is never
///     double-emitted as a layer bullet.
/// </summary>
public sealed class MyAppLayerSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        Layer web = arch.Layer("Web", "MyApp.Web.*");
        Layer billing = arch.Layer("Billing", "MyApp.Legacy.Billing.*");

        arch.Rule("layering/web-not-billing")
            .Enforce(web.MustNotReference(arch.Namespace("MyApp.Legacy.Billing.*")))
            .Because("The web layer must reach billing only through the sanctioned facade.");

        arch.Rule("layering/billing-not-web")
            .Enforce(billing.MustNotReference(arch.Namespace("MyApp.Web.*")))
            .Because("Billing must not reach up into the web layer.");

        arch.Scope("legacy/billing")
            .Freeze(billing)
            .BoundaryOnlyVia(typeof(IBillingFacade), typeof(BillingFacade))
            .Dragons("Banker's rounding happens at line-item level, NOT invoice level. " +
                     "Nightly reconciliation depends on this. Do not normalize.")
            .Because("Replacement scheduled (BillingV2, ADR-019); not worth stabilizing.");
    }
}