// Two types sharing the simple name `Order` in distinct namespaces, for the colliding-simple-name
// qualification pin (GRAMMAR §6): they render as `Billing.Order` / `Sales.Order`. Their members
// (Total, Refresh) back the member-collision pins for MustNotUse (GRAMMAR §4.5, §6).

namespace Zphil.LoadBearing.Tests.Stubs.Billing
{
    /// <summary>An order type in the Billing namespace.</summary>
    public sealed class Order
    {
        /// <summary>The order total.</summary>
        public decimal Total => 0m;
    }
}

namespace Zphil.LoadBearing.Tests.Stubs.Sales
{
    /// <summary>An order type in the Sales namespace.</summary>
    public sealed class Order
    {
        /// <summary>The order total.</summary>
        public decimal Total => 0m;

        /// <summary>Refreshes the order.</summary>
        public void Refresh()
        {
        }
    }
}