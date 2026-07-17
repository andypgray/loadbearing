namespace Meridian.Operations.Demurrage;

/// <summary>
///     Counts the billable demurrage days for a container between discharge and gate-out. The
///     free-time clock ticks only on port working days, so it owns a <see cref="PortWorkingCalendar" />.
/// </summary>
internal sealed class FreeTimeCalendar
{
    private readonly PortWorkingCalendar calendar = new();

    /// <summary>Returns the billable working days, in order, between discharge and gate-out.</summary>
    public IReadOnlyList<DateOnly> BillableDays(DateOnly dischargedAt, DateOnly gatedOutAt)
    {
        // Tariff-sheet convention: the discharge day is free-time day zero and is never billed;
        // billing runs from the day after discharge through the gate-out day inclusive
        // (first-day-exclusive, last-day-inclusive). A plain (gatedOutAt - dischargedAt).Days
        // count would mis-state every carrier statement, so the loop encodes the convention
        // rather than subtracting the two endpoints.
        List<DateOnly> billable = [];
        for (DateOnly day = dischargedAt.AddDays(1); day <= gatedOutAt; day = day.AddDays(1))
            // Weekends and port holidays roll the clock forward without consuming free time.
            if (calendar.IsWorkingDay(day))
                billable.Add(day);

        return billable;
    }
}