namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     Rendered agent-context text that more than one suite is pinned against — a family rule's bullet,
///     which appears on both of its cells' cards, and the whole cards the MCP surface returns and the
///     render verb splices into a file. Spelled once because two copies of a string would let one drift:
///     the CLI-versus-MCP parity rows and the end-to-end render goldens only mean the same thing while
///     the text they compare is literally the same text.
/// </summary>
/// <remarks>
///     The cards here carry no provenance line — that is a render file-splice concern rather than part of
///     the card — so an MCP row uses one bare and a render row prepends its own spec's provenance.
/// </remarks>
internal static class RenderedLawText
{
    internal const string LeavesBullet =
        "- `layering/leaves-independent` — Each of the Reporting and Billing layers must not reference " +
        "the others. Reporting and billing are the two leaves of this solution; neither may grow a " +
        "dependency on the other.\n";

    internal const string LeavesNotCircularBullet =
        "- `layering/leaves-not-circular` — Each of the Reporting and Billing layers must not have " +
        "circular references with the others. Reporting and billing may only ever point one way; a circle " +
        "between the two leaves would make the reporting slice part of the legacy biller.\n";

    /// <summary>MyAppRenderSpec's quarantined <c>legacy/billing</c> scope card.</summary>
    internal const string BillingScopeCard =
        "## Quarantined scope `legacy/billing`\n\n" +
        "This directory holds the quarantined `legacy/billing` scope: types in `MyApp.Legacy.Billing.*`. " +
        "Here be dragons — do not spread references into it.\n\n" +
        "Dragons: Banker's rounding happens at line-item level, NOT invoice level. " +
        "Nightly reconciliation depends on this. Do not normalize.\n\n" +
        "- `legacy/billing/containment` — Types in `MyApp.Legacy.Billing.*`, except `IBillingFacade` or " +
        "`BillingFacade`, must be referenced only by types in `MyApp.Legacy.Billing.*`, `IBillingFacade` or " +
        "`BillingFacade`. Replacement scheduled (BillingV2, ADR-019); not worth stabilizing.\n" +
        "- Sanctioned surface: `IBillingFacade`, `BillingFacade`.\n" +
        "- Expand: `loadbearing explain legacy/billing/containment`.";

    /// <summary>
    ///     MyAppRenderSpec's cautioned <c>domain/retry-budget</c> scope card — the other scope posture over
    ///     the same spec. It names no boundary and never tells the reader to keep out, and its lede carries
    ///     the scoped selection because a caution has no containment sentence to carry it.
    /// </summary>
    internal const string DomainCautionCard =
        "## Cautioned scope `domain/retry-budget`\n\n" +
        "This directory holds the cautioned `domain/retry-budget` scope: types named `RetryPolicy`. " +
        "Here be dragons — the weirdness below is load-bearing; read it before you edit, and do not " +
        "tidy it away.\n\n" +
        "Dragons: RetryPolicy's broad catch is filtered on purpose: the `when` clause is what keeps it green " +
        "under the unfiltered-catch rule, and it is the fixture's one sanctioned broad handler. Keep the " +
        "filter; add cases beside it, never inside it.\n\n" +
        "- `domain/retry-budget/tripwire` — a change set touching this scope is flagged by " +
        "`check --diff-base <ref>`. The retry budget is the one place the domain sanctions a broad catch, " +
        "and every caller relies on the filter.\n" +
        "- Expand: `loadbearing explain domain/retry-budget/tripwire`.";

    /// <summary>
    ///     MyAppLayerSpec's two cards for the MyApp.Web directory, in declaration order: the Web layer,
    ///     defined as its project, and the Reporting layer that refines it. Reporting narrows Web rather than
    ///     moving anywhere, so both are placed at the same deepest common ancestor — which is why the render
    ///     verb merges them into one managed block and the context tool returns both.
    /// </summary>
    internal const string WebLayerCards =
        "## Layer `Web`\n\n" +
        "This directory holds the `Web` layer. The HTTP surface: controllers and the views they serve. " +
        "Its architecture rules:\n\n" +
        "- `layering/web-not-billing` — The Web layer must not reference types in `MyApp.Legacy.Billing.*`. " +
        "The web layer must reach billing only through the sanctioned facade.\n" +
        "- Expand any rule above with `loadbearing explain <rule-id>`.\n\n" +
        "## Layer `Reporting`\n\n" +
        "This directory holds the `Reporting` layer. Its architecture rules:\n\n" +
        "- `layering/reporting-not-billing` — The Reporting layer must not reference types in " +
        "`MyApp.Legacy.Billing.*`. The reporting slice takes its numbers from the domain, never from the " +
        "legacy biller.\n" +
        LeavesBullet +
        LeavesNotCircularBullet +
        "- Expand any rule above with `loadbearing explain <rule-id>`.";
}
