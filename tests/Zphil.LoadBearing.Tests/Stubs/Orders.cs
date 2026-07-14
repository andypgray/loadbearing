// Two types sharing the simple name `Order` in distinct namespaces, for the colliding-simple-name
// qualification pin (GRAMMAR §6): they render as `Billing.Order` / `Sales.Order`.

namespace Zphil.LoadBearing.Tests.Stubs.Billing
{
    /// <summary>An order type in the Billing namespace.</summary>
    public sealed class Order;
}

namespace Zphil.LoadBearing.Tests.Stubs.Sales
{
    /// <summary>An order type in the Sales namespace.</summary>
    public sealed class Order;
}