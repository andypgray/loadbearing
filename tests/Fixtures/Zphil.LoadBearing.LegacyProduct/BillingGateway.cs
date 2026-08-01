// C# 7.3 (net48's default language version) — braced namespaces on purpose; see the csproj.

namespace Legacy.Product
{
    /// <summary>
    ///     The concrete gateway a legacy spec bans direct construction of. Like
    ///     <see cref="IBillingGateway" /> it stays inside netstandard2.0, so a <c>typeof()</c> anchor on it
    ///     resolves in the net10 host.
    /// </summary>
    public class BillingGateway : IBillingGateway
    {
        public decimal Charge(string account, decimal amount)
        {
            return amount;
        }
    }
}
