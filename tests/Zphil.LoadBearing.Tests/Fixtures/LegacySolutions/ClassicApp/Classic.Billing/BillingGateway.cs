namespace Classic.Billing
{
    // The sanctioned route to the calculator: billing/no-direct-calculator excepts this type, so the
    // rule passes here and would red on any other caller.
    public class BillingGateway : IBillingGateway
    {
        private readonly BillingCalculator _calculator = new BillingCalculator();

        public decimal Charge(string account, decimal amount)
        {
            return _calculator.Total(account) + amount;
        }
    }
}
