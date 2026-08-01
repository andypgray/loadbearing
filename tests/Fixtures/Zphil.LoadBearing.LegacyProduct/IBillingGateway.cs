// C# 7.3 (net48's default language version) — braced namespaces on purpose; see the csproj.

namespace Legacy.Product
{
    /// <summary>
    ///     The sanctioned billing surface. Its whole type closure stays inside netstandard2.0, so a spec
    ///     may anchor on it with <c>typeof()</c> and the net10 host can load it through the spec ALC.
    /// </summary>
    public interface IBillingGateway
    {
        decimal Charge(string account, decimal amount);
    }
}
