namespace Meridian.Operations.Demurrage;

/// <summary>
///     The demurrage rate for a day, chosen by that day's position in the billable sequence.
/// </summary>
internal sealed class DemurrageTariffTable
{
    private const decimal FirstTierRate = 150m; // billable days 6-10
    private const decimal SecondTierRate = 275m; // billable days 11 onward

    /// <summary>Returns the rate for a billable day given its 1-based position in the billable sequence.</summary>
    public decimal RateForBillableDay(int billableDayOrdinal)
    {
        // Non-contiguous tiers keyed by ordinal, not by calendar span: the first five billable
        // days are free, days 6-10 bill at the first tier, day 11 onward at the second. The free
        // tier is the gap that makes this a stepped lookup rather than a single linear rate.
        if (billableDayOrdinal <= 5) return 0m;
        if (billableDayOrdinal <= 10) return FirstTierRate;
        return SecondTierRate;
    }
}