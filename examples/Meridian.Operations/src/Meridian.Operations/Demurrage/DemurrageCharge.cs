namespace Meridian.Operations.Demurrage;

/// <summary>
///     The day-by-day demurrage breakdown behind a charge. It never crosses the module boundary:
///     the <see cref="IDemurrageCalculator" /> facade returns only <see cref="Total" />.
/// </summary>
internal sealed record DemurrageCharge(string ContainerId)
{
    /// <summary>The billable working days and the tariff rate applied to each.</summary>
    public required IReadOnlyList<Day> Days { get; init; }

    /// <summary>The total charge: the sum of every billable day's rate.</summary>
    public decimal Total => Days.Sum(day => day.Rate);

    /// <summary>One billable working day: its date, its position in the billable sequence, and its rate.</summary>
    internal sealed record Day(DateOnly Date, int Ordinal, decimal Rate);
}