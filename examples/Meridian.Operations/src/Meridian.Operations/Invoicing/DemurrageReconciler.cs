using Meridian.Operations.Demurrage;

namespace Meridian.Operations.Invoicing;

/// <summary>
///     Builds the per-day demurrage dispute report that the calculator facade does not expose.
/// </summary>
internal sealed class DemurrageReconciler
{
    /// <summary>Returns the exact working days billed between discharge and gate-out, for a customer dispute.</summary>
    public IReadOnlyList<DateOnly> DisputedDays(DateOnly dischargedAt, DateOnly gatedOutAt)
    {
        // Deliberate boundary breach: IDemurrageCalculator returns only the total, but a customer
        // dispute needs the exact days that were billed. Those day-by-day figures predate the
        // module boundary and live only in FreeTimeCalendar, so this reconciler constructs it
        // directly rather than through the facade. A sanctioned per-day contract on the demurrage
        // module would let this reference go.
        var calendar = new FreeTimeCalendar();
        return calendar.BillableDays(dischargedAt, gatedOutAt);
    }
}