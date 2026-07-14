using System;

namespace MyApp.Legacy.Billing;

public class BillingCalculator
{
    public decimal RoundLineItem(decimal amount, RoundingMode mode)
    {
        if (mode == RoundingMode.Bankers)
            return Math.Round(amount, 2, MidpointRounding.ToEven);
        return Math.Round(amount, 2);
    }
}
