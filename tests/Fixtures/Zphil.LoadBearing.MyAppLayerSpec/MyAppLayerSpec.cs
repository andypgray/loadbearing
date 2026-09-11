using MyApp.Legacy.Billing;

namespace Zphil.LoadBearing.MyAppLayerSpec;

/// <summary>
///     The layer-card render fixture, and the fixture where all three definition forms stand side by side:
///     Web is its project, Billing is a namespace glob, and Reporting is a refinement of Web. Each carries
///     one anchored Enforce rule, and the canonical billing quarantine sits over the Billing layer.
///     Rendered against the MyApp solution it produces the root block, a merged block in <c>MyApp.Web/</c>
///     whose Web card precedes the Reporting card (declaration order), and — in
///     <c>MyApp.Legacy.Billing/</c> — a merged block whose Billing layer card precedes the
///     quarantined-scope card (layer key before quarantine key). That Billing directory is the
///     layer-and-scope acceptance surface; the quarantined layer's desugared containment is
///     layer-anchored yet Quarantine-posture, so it renders only as the quarantine card and is never
///     double-emitted as a layer bullet. Web carries a purpose and Billing does not, so the Web card's lede
///     carries the sentence and the Billing card's does not.
///     <para>
///         It is also where <em>family</em> rules are rendered: <c>layering/leaves-independent</c> and
///         <c>layering/leaves-not-circular</c> are two laws over the same Reporting and Billing cells, and
///         they are here to prove the placement decision — a family of layers anchors on every cell, so
///         both bullets appear on the Reporting card in <c>MyApp.Web/</c> and on the Billing card in
///         <c>MyApp.Legacy.Billing/</c>. Both pass, which is what keeps this fixture's every rule green.
///     </para>
/// </summary>
/// <remarks>
///     Web is anchored on its project rather than on <c>MyApp.Web.*</c> deliberately: the two name the same
///     types here, so every card, sentence and verdict below is a byte-for-byte statement that a layer
///     checks and renders as whatever defines it. Reporting is the refinement — the shape a cone nested
///     inside another needs — and it narrows Web to the four <c>Report*</c> types, none of which touches
///     billing, so the Reporting rule holds where Web's does not.
/// </remarks>
public sealed class MyAppLayerSpec : IArchitectureSpec
{
    /// <inheritdoc />
    public void Define(Arch arch)
    {
        Layer web = arch.Layer("Web", arch.Project("MyApp.Web")).Purpose("The HTTP surface: controllers and the views they serve.");
        Layer billing = arch.Layer("Billing", "MyApp.Legacy.Billing.*");
        Layer reporting = arch.Layer("Reporting", web.WithPrefix("Report"));

        arch.Rule("layering/web-not-billing")
            .Enforce(web.MustNotReference(arch.Namespace("MyApp.Legacy.Billing.*")))
            .Because("The web layer must reach billing only through the sanctioned facade.");

        arch.Rule("layering/billing-not-web")
            .Enforce(billing.MustNotReference(arch.Namespace("MyApp.Web.*")))
            .Because("Billing must not reach up into the web layer.");

        arch.Rule("layering/reporting-not-billing")
            .Enforce(reporting.MustNotReference(arch.Namespace("MyApp.Legacy.Billing.*")))
            .Because("The reporting slice takes its numbers from the domain, never from the legacy biller.");

        // Passes, and the fixture's first family rule: one law over a two-cell partition, whose bullet
        // lands on BOTH cells' cards — Reporting's in MyApp.Web/ and Billing's in MyApp.Legacy.Billing/.
        arch.Rule("layering/leaves-independent")
            .Enforce(arch.Each(reporting, billing).MustNotReferenceEachOther())
            .Because("Reporting and billing are the two leaves of this solution; neither may grow a dependency on the other.");

        // Passes, and the fixture's second family rule: the two rules above already pin Reporting and Billing one-way,
        // so no circle can close, and its bullet joins the leaves bullet on both cell cards.
        arch.Rule("layering/leaves-not-circular")
            .Enforce(arch.Each(reporting, billing).MustNotHaveCircularReferences())
            .Because("Reporting and billing may only ever point one way; a circle between the two leaves would make the reporting slice part of the legacy biller.");

        arch.Scope("legacy/billing")
            .Quarantine(billing)
            .BoundaryOnlyVia(typeof(IBillingFacade), typeof(BillingFacade))
            .Dragons("Banker's rounding happens at line-item level, NOT invoice level. " +
                     "Nightly reconciliation depends on this. Do not normalize.")
            .Because("Replacement scheduled (BillingV2, ADR-019); not worth stabilizing.");
    }
}
