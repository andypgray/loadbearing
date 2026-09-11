using MyApp.Legacy.Billing;

namespace Zphil.LoadBearing.MyAppQuarantinedSpec;

/// <summary>
///     A minimal quarantined-scope spec for the MyApp fixture: a single Quarantine scope over
///     <c>MyApp.Legacy.Billing</c> with a sanctioned facade surface and <em>no</em> explicit
///     <c>.Baseline</c>.
/// </summary>
/// <remarks>
///     <para>
///         Its containment therefore resolves to the conventional default
///         <c>arch/baselines/legacy/billing/containment.json</c> — the committed grandfather baseline that
///         records InvoiceController's pre-existing interior references. Against it, <c>check</c> is exit 0
///         (containment passes; the tripwire skips without a <c>--diff-base</c>) — the grandfathered-quarantine
///         e2e. Deliberately no ordinary rules, so the whole-run exit code is containment-driven.
///     </para>
///     <para>
///         A second scope carries the field's sanctioned-consumer shape (GRAMMAR §7): its boundary is a type
///         <em>outside</em> the scope, named rather than anchored by <c>typeof</c>, and it is the only thing
///         that reaches in — so it passes uncaptured, with no baseline of its own.
///     </para>
///     <para>
///         A third carries the Caution posture, which has no red state at all and therefore never moves the
///         exit code — which is why the diff-aware tripwire e2e can prove "warns and exits 0" here, on the
///         one spec whose whole-run verdict is green to begin with.
///     </para>
/// </remarks>
public sealed class MyAppQuarantinedSpec : IArchitectureSpec
{
    /// <inheritdoc />
    public void Define(Arch arch)
    {
        arch.Scope("legacy/billing")
            .Quarantine(arch.Namespace("MyApp.Legacy.Billing.*"))
            .BoundaryOnlyVia(typeof(IBillingFacade), typeof(BillingFacade))
            .Dragons("Banker's rounding happens at line-item level, NOT invoice level. Do not normalize.")
            .Because("Replacement scheduled; not worth stabilizing.");

        arch.Scope("legacy/web-shell")
            .Quarantine(arch.Namespace("MyApp.Web.*"))
            .BoundaryOnlyVia(arch.Types.Named("OrderService"))
            .Dragons("The shell's rendering path is order-sensitive. Drive it through OrderService only.")
            .Because("The shell predates the current stack; a rewrite is scheduled.");

        // The caution: one tripwire and nothing else, over a type in MyApp.Domain — a project neither
        // quarantine touches, so a diff that reaches it fires this warning alone. Together with the two
        // scopes above it makes this spec the diff-aware bed for both tripwire wordings on one run.
        arch.Scope("domain/retry-budget")
            .Caution(arch.Types.Named("RetryPolicy"))
            .Dragons("RetryPolicy's broad catch is filtered on purpose: the `when` clause is what keeps it green " +
                     "under the unfiltered-catch rule, and it is the fixture's one sanctioned broad handler. Keep the " +
                     "filter; add cases beside it, never inside it.")
            .Because("The retry budget is the one place the domain sanctions a broad catch, and every caller relies on the filter.");
    }
}
