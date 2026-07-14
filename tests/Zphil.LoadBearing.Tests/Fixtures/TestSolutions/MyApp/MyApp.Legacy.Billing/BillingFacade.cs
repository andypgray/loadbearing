namespace MyApp.Legacy.Billing;

public class BillingFacade : IBillingFacade
{
    private readonly BillingCalculator calculator = new BillingCalculator();

    public decimal CalculateTotal()
    {
        return calculator.RoundLineItem(100.5m, RoundingMode.Bankers);
    }
}
