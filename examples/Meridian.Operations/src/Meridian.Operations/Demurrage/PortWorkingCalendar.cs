namespace Meridian.Operations.Demurrage;

/// <summary>
///     Port working days. On weekends and on the port holidays this calendar owns, the free-time
///     clock does not advance.
/// </summary>
internal sealed class PortWorkingCalendar
{
    // Port holidays are reference data the demurrage engine owns; a real deployment would load
    // them per port, but a fixed set keeps the example self-contained.
    private static readonly HashSet<DateOnly> Holidays =
    [
        new(2026, 3, 6),
        new(2026, 12, 25)
    ];

    /// <summary>True when the date is a weekday that is not a port holiday.</summary>
    public bool IsWorkingDay(DateOnly date)
    {
        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;
        return !Holidays.Contains(date);
    }
}