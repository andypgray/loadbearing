using MyApp.Legacy.Billing;

namespace Zphil.LoadBearing.MyAppQuarantinedSpec;

/// <summary>
///     A minimal quarantined-scope spec for the MyApp fixture: a single Quarantine scope over
///     <c>MyApp.Legacy.Billing</c> with a sanctioned facade surface and <em>no</em> explicit
///     <c>.Baseline</c>. Its containment therefore resolves to the conventional default
///     <c>arch/baselines/legacy/billing/containment.json</c> — the committed grandfather baseline that
///     records InvoiceController's pre-existing interior references. Against it, <c>check</c> is exit 0
///     (containment passes; the tripwire skips without a <c>--diff-base</c>) — the grandfathered-quarantine
///     e2e. Deliberately no other rules, so the whole-run exit code is containment-driven.
/// </summary>
public sealed class MyAppQuarantinedSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Scope("legacy/billing")
            .Quarantine(arch.Namespace("MyApp.Legacy.Billing.*"))
            .BoundaryOnlyVia(typeof(IBillingFacade), typeof(BillingFacade))
            .Dragons("Banker's rounding happens at line-item level, NOT invoice level. Do not normalize.")
            .Because("Replacement scheduled; not worth stabilizing.");
    }
}
