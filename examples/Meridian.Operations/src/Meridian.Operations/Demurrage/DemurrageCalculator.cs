namespace Meridian.Operations.Demurrage;

/// <summary>
///     Wires the free-time calendar to the tariff table and returns only the total charge. This
///     is the one type outside the module that anything is meant to touch; the calendar, the
///     tariff, and the <see cref="DemurrageCharge" /> breakdown it builds are all internal.
/// </summary>
public sealed class DemurrageCalculator : IDemurrageCalculator
{
    private readonly FreeTimeCalendar calendar = new();
    private readonly DemurrageTariffTable tariff = new();

    public decimal CalculateCharge(string containerId, DateOnly dischargedAt, DateOnly gatedOutAt)
    {
        var billableDays = calendar.BillableDays(dischargedAt, gatedOutAt);

        List<DemurrageCharge.Day> days = [];
        for (var i = 0; i < billableDays.Count; i++)
        {
            int ordinal = i + 1;
            decimal rate = tariff.RateForBillableDay(ordinal);
            days.Add(new DemurrageCharge.Day(billableDays[i], ordinal, rate));
        }

        var charge = new DemurrageCharge(containerId) { Days = days };
        return charge.Total;
    }
}