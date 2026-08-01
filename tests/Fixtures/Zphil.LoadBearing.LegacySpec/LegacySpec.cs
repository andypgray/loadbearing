// C# 7.3 (net48's default language version) — braced namespaces on purpose; see the csproj.

using Legacy.Product;

namespace Zphil.LoadBearing.LegacySpec
{
    /// <summary>
    ///     A spec compiled for net48 at C# 7.3, anchored with <c>typeof()</c> on net48 product types. It is
    ///     the fixture behind the cross-platform half of the legacy proof: the net10 host loads this DLL in
    ///     the collectible spec ALC, the shared contract resolves from the Default context so
    ///     <c>spec is IArchitectureSpec</c> holds, and the sibling product assembly resolves app-locally
    ///     with no <c>.deps.json</c> to guide it.
    /// </summary>
    /// <remarks>
    ///     Both anchors below stay inside the netstandard2.0 surface, which is the condition on
    ///     <c>typeof()</c> from a Framework spec. <c>Legacy.Product.LegacyHandler</c>, whose interface
    ///     lives in <c>System.Web</c>, is the other side of that line and is reachable only by pattern.
    /// </remarks>
    public sealed class LegacySpec : IArchitectureSpec
    {
        /// <inheritdoc />
        public void Define(Arch arch)
        {
            arch.Rule("legacy/interfaces")
                .Enforce(arch.Types.OfKind(TypeKind.Interface).InNamespace("Legacy.*").MustHavePrefix("I"))
                .Because("An I-prefixed interface is what the rest of this codebase reads as a contract.");

            arch.Rule("legacy/gateway-through-interface")
                .Enforce(arch.Types.InNamespace("Legacy.*").MustNotConstruct(typeof(BillingGateway)))
                .Because("Callers depend on the billing contract, not on the concrete gateway that " +
                         "carries the connection settings.")
                .Fix("Take an IBillingGateway and let the composition root supply the implementation.");
        }
    }
}
