namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     Rendered law text that more than one suite is pinned against. A family rule's bullet appears
///     on both of its cells' cards and on the MCP surface as well as the render one, so it is spelled
///     once: two copies of a string would let one drift.
/// </summary>
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
}
