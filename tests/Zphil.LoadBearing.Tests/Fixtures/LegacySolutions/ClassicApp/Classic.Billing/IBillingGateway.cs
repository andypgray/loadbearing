namespace Classic.Billing
{
    public interface IBillingGateway
    {
        decimal Charge(string account, decimal amount);
    }

    // Seeded violation: a contract declared without the I prefix.
    public interface BillingSink
    {
        void Record(string account, decimal amount);
    }
}
