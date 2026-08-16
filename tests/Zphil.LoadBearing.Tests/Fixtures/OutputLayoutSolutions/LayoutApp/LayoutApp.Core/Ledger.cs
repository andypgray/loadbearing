namespace LayoutApp.Core
{
    // The checked universe's only implementation: enough that the model is not empty once SpecExclusion has
    // dropped the spec project.
    public sealed class Ledger : ILedger
    {
        public decimal Balance { get; private set; }

        public void Post(decimal amount)
        {
            Balance += amount;
        }
    }
}
